using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace BeamRunner.Services
{
    /// <summary>
    /// Full-screen "SIMULATED AD" overlay built in code with uGUI (no prefabs, no TextMeshPro). Shows a countdown
    /// (unscaled time, so it runs while the game is paused), a Skip button that is available at once and a Close
    /// button that unlocks when the countdown ends. Escape / Back / gamepad East = Skip; Enter / Space /
    /// gamepad South = Close. Creates a temporary EventSystem only if the scene has none.
    /// </summary>
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    public sealed class SimulatedAdOverlay : MonoBehaviour
    {
        private const int CanvasSortingOrder = 32000;
        private const string FontResourcePath = "BeamRunner/Fonts/Rajdhani-Bold";

        private static readonly Color BackdropColor = new Color(0.02f, 0.01f, 0.06f, 0.97f);
        private static readonly Color BorderColor = new Color(1f, 0.18f, 0.78f, 1f);
        private static readonly Color PanelColor = new Color(0.07f, 0.03f, 0.14f, 1f);
        private static readonly Color TitleColor = new Color(0.25f, 0.95f, 1f, 1f);
        private static readonly Color BodyColor = new Color(0.78f, 0.74f, 0.9f, 1f);
        private static readonly Color SkipColor = new Color(0.22f, 0.2f, 0.32f, 1f);
        private static readonly Color CloseColor = new Color(0.95f, 0.16f, 0.7f, 1f);

        private static Font s_font;

        private Action<bool> _onFinished;
        private bool _rewarded;
        private bool _finished;
        private float _remaining;
        private int _shownSeconds = -1;
        private Text _countdownLabel;
        private Button _closeButton;
        private GameObject _ownedEventSystem;

        private const int ShortcutNone = 0;
        private const int ShortcutSkip = 1;
        private const int ShortcutClose = 2;
        private int _pendingShortcut;

        /// <summary>
        /// Builds and shows the overlay. <paramref name="onFinished"/> receives true when closed after the
        /// countdown (a completed ad) and false when skipped. Returns null if the overlay could not be built
        /// (the callback is then not invoked).
        /// </summary>
        public static SimulatedAdOverlay Create(bool rewarded, float countdownSeconds, Action<bool> onFinished)
        {
            GameObject root = null;
            SimulatedAdOverlay overlay = null;
            try
            {
                root = new GameObject("[BeamRunner.SimulatedAd]", typeof(RectTransform));
                DontDestroyOnLoad(root);
                overlay = root.AddComponent<SimulatedAdOverlay>();
                overlay.Build(rewarded, countdownSeconds, onFinished);
                return overlay;
            }
            catch (Exception e)
            {
                ServiceLog.Exception("Building the simulated ad overlay failed", e);
                if (overlay != null)
                {
                    overlay._onFinished = null;
                    overlay._finished = true;
                    overlay.DestroyOwnedEventSystem();
                }

                if (root != null)
                {
                    Destroy(root);
                }

                return null;
            }
        }

        private void Build(bool rewarded, float countdownSeconds, Action<bool> onFinished)
        {
            _rewarded = rewarded;
            _onFinished = onFinished;
            _remaining = Mathf.Max(0.5f, countdownSeconds);

            EnsureEventSystem();

            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = CanvasSortingOrder;

            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            gameObject.AddComponent<GraphicRaycaster>();

            var root = (RectTransform)transform;
            Font font = LoadFont();

            Image backdrop = CreateImage("Backdrop", root, BackdropColor);
            Stretch(backdrop.rectTransform);
            backdrop.raycastTarget = true; // swallow all input aimed at the game underneath

            Image border = CreateImage("Border", root, BorderColor);
            Place(border.rectTransform, Vector2.zero, new Vector2(912f, 972f));

            Image panel = CreateImage("Panel", root, PanelColor);
            Place(panel.rectTransform, Vector2.zero, new Vector2(900f, 960f));
            RectTransform panelRect = panel.rectTransform;

            Text title = CreateText("Title", panelRect, font, "SIMULATED AD", 104, TitleColor);
            Place(title.rectTransform, new Vector2(0f, 330f), new Vector2(860f, 150f));

            string kind = rewarded ? "Rewarded video" : "Interstitial";
            Text subtitle = CreateText("Subtitle", panelRect, font,
                kind + "\n<size=34>Editor / development build - no ad network configured</size>", 52, BodyColor);
            subtitle.supportRichText = true;
            Place(subtitle.rectTransform, new Vector2(0f, 160f), new Vector2(860f, 170f));

            _countdownLabel = CreateText("Countdown", panelRect, font, "", 64, Color.white);
            Place(_countdownLabel.rectTransform, new Vector2(0f, -20f), new Vector2(860f, 140f));

            Button skip = CreateButton("SkipButton", panelRect, font, "SKIP", SkipColor, new Vector2(-215f, -320f));
            skip.onClick.AddListener(Skip);

            _closeButton = CreateButton("CloseButton", panelRect, font, "CLOSE", CloseColor, new Vector2(215f, -320f));
            _closeButton.onClick.AddListener(Close);
            _closeButton.interactable = false;

            RefreshCountdown();
        }

        private void Update()
        {
            if (_finished)
            {
                return;
            }

            if (_remaining > 0f)
            {
                _remaining -= Time.unscaledDeltaTime;
                if (_remaining <= 0f)
                {
                    _remaining = 0f;
                    if (_closeButton != null)
                    {
                        _closeButton.interactable = true;
                    }
                }

                RefreshCountdown();
            }

            HandleShortcuts();
        }

        private void LateUpdate()
        {
            // Keyboard/gamepad shortcuts finish here, after every Update: the game's UI polls the same keys
            // (Escape / East = back) in its own Update. Finishing inside Update would, depending on script order,
            // let that UI see the ad as already gone and act on the same press too (e.g. skip the revive ad AND
            // decline the revive). Deferred, every UI Update still sees IsShowing == true this frame.
            int shortcut = _pendingShortcut;
            _pendingShortcut = ShortcutNone;
            if (shortcut == ShortcutSkip)
            {
                Skip();
            }
            else if (shortcut == ShortcutClose)
            {
                Close();
            }
        }

        private void HandleShortcuts()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.escapeKey.wasPressedThisFrame)
                {
                    _pendingShortcut = ShortcutSkip;
                    return;
                }

                if (_remaining <= 0f && (keyboard.enterKey.wasPressedThisFrame || keyboard.spaceKey.wasPressedThisFrame))
                {
                    _pendingShortcut = ShortcutClose;
                    return;
                }
            }

            Gamepad gamepad = Gamepad.current;
            if (gamepad != null)
            {
                if (gamepad.buttonEast.wasPressedThisFrame)
                {
                    _pendingShortcut = ShortcutSkip;
                }
                else if (_remaining <= 0f && gamepad.buttonSouth.wasPressedThisFrame)
                {
                    _pendingShortcut = ShortcutClose;
                }
            }
        }

        private void RefreshCountdown()
        {
            if (_countdownLabel == null)
            {
                return;
            }

            int seconds = Mathf.CeilToInt(_remaining);
            if (seconds == _shownSeconds)
            {
                return;
            }

            _shownSeconds = seconds;
            if (seconds > 0)
            {
                _countdownLabel.text = (_rewarded ? "Reward in " : "Close in ") + seconds;
            }
            else
            {
                _countdownLabel.text = _rewarded ? "Reward earned - tap CLOSE" : "Tap CLOSE to continue";
            }
        }

        private void Skip()
        {
            // Skipping a rewarded ad forfeits the reward; for interstitials the result is irrelevant.
            Finish(!_rewarded);
        }

        private void Close()
        {
            if (_remaining > 0f)
            {
                return;
            }

            Finish(true);
        }

        private void Finish(bool completed)
        {
            if (_finished)
            {
                return;
            }

            _finished = true;
            Action<bool> callback = _onFinished;
            _onFinished = null;

            DestroyOwnedEventSystem();
            Destroy(gameObject);

            if (callback != null)
            {
                try
                {
                    callback(completed);
                }
                catch (Exception e)
                {
                    ServiceLog.Exception("Simulated ad callback failed", e);
                }
            }
        }

        private void OnDestroy()
        {
            if (_finished)
            {
                return;
            }

            // Destroyed externally (scene teardown, play mode exit): report a non-completed ad.
            _finished = true;
            DestroyOwnedEventSystem();
            Action<bool> callback = _onFinished;
            _onFinished = null;
            if (callback != null && !ServiceRunner.IsQuitting)
            {
                ServiceRunner.Post(() => callback(false));
            }
        }

        private void EnsureEventSystem()
        {
            if (EventSystem.current != null)
            {
                return;
            }

            var go = new GameObject("[BeamRunner.SimulatedAdEventSystem]");
            DontDestroyOnLoad(go);
            go.AddComponent<EventSystem>();
            go.AddComponent<InputSystemUIInputModule>();
            _ownedEventSystem = go;
        }

        private void DestroyOwnedEventSystem()
        {
            if (_ownedEventSystem != null)
            {
                Destroy(_ownedEventSystem);
                _ownedEventSystem = null;
            }
        }

        private static Font LoadFont()
        {
            if (s_font != null)
            {
                return s_font;
            }

            try
            {
                s_font = Resources.Load<Font>(FontResourcePath);
            }
            catch (Exception)
            {
                s_font = null;
            }

            if (s_font == null)
            {
                try
                {
                    s_font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                }
                catch (Exception)
                {
                    s_font = null;
                }
            }

            return s_font;
        }

        private static Image CreateImage(string name, RectTransform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static Text CreateText(string name, RectTransform parent, Font font, string value, int size, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<Text>();
            if (font != null)
            {
                text.font = font;
            }

            text.text = value;
            text.fontSize = size;
            text.color = color;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.supportRichText = false;
            text.raycastTarget = false;
            return text;
        }

        private static Button CreateButton(string name, RectTransform parent, Font font, string label, Color color,
            Vector2 position)
        {
            Image image = CreateImage(name, parent, color);
            image.raycastTarget = true;
            Place(image.rectTransform, position, new Vector2(380f, 132f));

            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            ColorBlock colors = button.colors;
            colors.highlightedColor = new Color(1f, 1f, 1f, 1f);
            colors.pressedColor = new Color(0.75f, 0.75f, 0.75f, 1f);
            colors.disabledColor = new Color(0.45f, 0.45f, 0.45f, 0.5f);
            button.colors = colors;

            Text text = CreateText("Label", image.rectTransform, font, label, 54, Color.white);
            Stretch(text.rectTransform);
            return button;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void Place(RectTransform rect, Vector2 anchoredPosition, Vector2 size)
        {
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = anchoredPosition;
        }
    }
}

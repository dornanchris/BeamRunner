using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace BeamRunner.UI
{
    /// <summary>Draw-order layers of the UI root (back to front).</summary>
    public enum UILayer
    {
        /// <summary>Full-screen backdrops (ignores the safe area).</summary>
        Background = 0,

        /// <summary>Menu screens (safe area).</summary>
        Screens = 1,

        /// <summary>In-run HUD (safe area).</summary>
        HUD = 2,

        /// <summary>Popups, floating texts, reward bursts (safe area).</summary>
        Popups = 3,

        /// <summary>Modal dialogs with full-screen dimmer.</summary>
        Dialogs = 4,

        /// <summary>Toast notifications (safe area).</summary>
        Toasts = 5,

        /// <summary>Full-screen fades (ignores the safe area).</summary>
        Overlay = 6,

        /// <summary>Debug widgets such as the FPS counter (safe area).</summary>
        Debug = 7,
    }

    /// <summary>
    /// Root of the UI: a screen-space-overlay canvas with a CanvasScaler (reference 1080×1920; matches width in portrait,
    /// height in landscape, blending on squarer tablets; re-evaluated when the screen changes), one nested canvas per
    /// <see cref="UILayer"/> (so HUD updates don't rebuild menus), safe-area handling, an EventSystem with the Input
    /// System UI module (created only if none exists), plus the shared <see cref="UIStack"/>, <see cref="Toasts"/>,
    /// <see cref="Dialogs"/>, <see cref="ScreenFader"/> and the progressive <see cref="UIBuildQueue"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UIRoot : MonoBehaviour
    {
        public static readonly Vector2 ReferenceResolution = new Vector2(1080f, 1920f);
        private static readonly int LayerCount = Enum.GetValues(typeof(UILayer)).Length;

        private static UIRoot _instance;

        private Canvas _canvas;
        private CanvasScaler _scaler;
        private RectTransform[] _layers;
        private bool[] _layerSafe;
        private UIStack _stack;
        private Toasts _toasts;
        private Dialogs _dialogs;
        private ScreenFader _fader;
        private UIBuildQueue _buildQueue;
        private FpsCounter _fps;
        private int _lastWidth;
        private int _lastHeight;
        private Rect _lastSafe;
        private bool _landscape;
        private int _layoutEventFrame = -1;

        /// <summary>The active root (null until <see cref="Create"/> is called).</summary>
        public static UIRoot Instance
        {
            get { return _instance; }
        }

        /// <summary>
        /// Raised the frame after the screen size, orientation or safe area changed, once the CanvasScaler has applied the
        /// new scale (safe-area anchors and <see cref="IsLandscape"/> are already updated).
        /// </summary>
        public event Action LayoutChanged;

        public Canvas Canvas
        {
            get { return _canvas; }
        }

        public CanvasScaler Scaler
        {
            get { return _scaler; }
        }

        public RectTransform Rect
        {
            get { return (RectTransform)transform; }
        }

        public UIStack Stack
        {
            get { return _stack; }
        }

        public Toasts Toasts
        {
            get { return _toasts; }
        }

        public Dialogs Dialogs
        {
            get { return _dialogs; }
        }

        public ScreenFader Fader
        {
            get { return _fader; }
        }

        /// <summary>Progressive builder for deferred screens and pool warm-ups (a few ms per frame).</summary>
        public UIBuildQueue BuildQueue
        {
            get { return _buildQueue; }
        }

        /// <summary>True when the screen is wider than tall.</summary>
        public bool IsLandscape
        {
            get { return _landscape; }
        }

        /// <summary>Canvas size in units (e.g. 1080×2340 on a 19.5:9 portrait phone).</summary>
        public Vector2 CanvasSize
        {
            get { return Rect.rect.size; }
        }

        /// <summary>
        /// Creates the UI root (or returns the existing one). <paramref name="sortingOrder"/> places the overlay canvas
        /// relative to other overlay canvases.
        /// </summary>
        public static UIRoot Create(int sortingOrder = 100, Transform parent = null)
        {
            if (_instance != null)
            {
                return _instance;
            }

            var go = new GameObject("FieldUI", typeof(RectTransform));
            go.layer = 5;
            if (parent != null)
            {
                go.transform.SetParent(parent, false);
            }

            UIRoot root = go.AddComponent<UIRoot>();
            root.Setup(sortingOrder);
            return root;
        }

        /// <summary>Returns the root, creating a default one if needed.</summary>
        public static UIRoot Ensure()
        {
            return _instance != null ? _instance : Create();
        }

        /// <summary>Container for a layer (safe-area layers are inset to <c>Screen.safeArea</c>).</summary>
        public RectTransform GetLayer(UILayer layer)
        {
            int i = (int)layer;
            return i >= 0 && i < _layers.Length ? _layers[i] : _layers[(int)UILayer.Screens];
        }

        /// <summary>Shows or hides the debug FPS counter.</summary>
        public void ShowFps(bool show)
        {
            if (show && _fps == null)
            {
                _fps = FpsCounter.Create(GetLayer(UILayer.Debug));
            }

            if (_fps != null)
            {
                _fps.gameObject.SetActive(show);
            }
        }

        private void Setup(int sortingOrder)
        {
            _instance = this;

            _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = sortingOrder;
            _canvas.pixelPerfect = false;

            _scaler = gameObject.AddComponent<CanvasScaler>();
            _scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            _scaler.referenceResolution = ReferenceResolution;
            _scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            _scaler.referencePixelsPerUnit = 100f;

            // Root raycaster first: nested raycasters use it as their root so results sort by depth across layers.
            gameObject.AddComponent<GraphicRaycaster>();

            _layers = new RectTransform[LayerCount];
            _layerSafe = new bool[LayerCount];
            for (int i = 0; i < LayerCount; i++)
            {
                var layer = (UILayer)i;
                RectTransform rt = UIFactory.CreateFill(transform, layer.ToString());
                Canvas c = rt.gameObject.AddComponent<Canvas>();
                c.overrideSorting = false;
                if (layer != UILayer.Background)
                {
                    rt.gameObject.AddComponent<GraphicRaycaster>();
                }

                _layers[i] = rt;
                _layerSafe[i] = layer != UILayer.Background && layer != UILayer.Overlay && layer != UILayer.Dialogs;
            }

            EnsureEventSystem();
            ApplyScreen(true);

            _stack = gameObject.AddComponent<UIStack>();
            _stack.Init(this);
            _buildQueue = gameObject.AddComponent<UIBuildQueue>();
            _toasts = GetLayer(UILayer.Toasts).gameObject.AddComponent<Toasts>();
            _dialogs = GetLayer(UILayer.Dialogs).gameObject.AddComponent<Dialogs>();
            _fader = ScreenFader.Create(GetLayer(UILayer.Overlay));
        }

        private static void EnsureEventSystem()
        {
            if (EventSystem.current != null)
            {
                return;
            }

            var go = new GameObject("EventSystem");
            EventSystem es = go.AddComponent<EventSystem>();
            // Added at runtime, the module assigns its default UI actions in OnEnable.
            go.AddComponent<InputSystemUIInputModule>();
            float dpi = Screen.dpi > 0f ? Screen.dpi : 160f;
            es.pixelDragThreshold = Mathf.Max(8, Mathf.RoundToInt(dpi * 0.06f));
            es.sendNavigationEvents = true;
        }

        private void Update()
        {
            // LayoutChanged is raised one frame after the change: CanvasScaler applies the new match/scale only in
            // Canvas.preWillRenderCanvases (after LateUpdate), so handlers that read rect sizes (HUD meter/Overdrive
            // placement) would otherwise lay out against the previous canvas size.
            if (_layoutEventFrame >= 0 && Time.frameCount > _layoutEventFrame)
            {
                _layoutEventFrame = -1;
                RaiseLayoutChanged();
            }

            ApplyScreen(false);
        }

        private void RaiseLayoutChanged()
        {
            Action handler = LayoutChanged;
            if (handler != null)
            {
                try
                {
                    handler();
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogException(e);
                }
            }
        }

        private void ApplyScreen(bool force)
        {
            int w = Screen.width;
            int h = Screen.height;
            Rect safe = Screen.safeArea;
            if (!force && w == _lastWidth && h == _lastHeight && safe == _lastSafe)
            {
                return;
            }

            _lastWidth = w;
            _lastHeight = h;
            _lastSafe = safe;
            if (w <= 0 || h <= 0)
            {
                return;
            }

            _landscape = w > h;
            float match;
            if (_landscape)
            {
                match = 1f;
            }
            else
            {
                // Tall phones match width; squarer (tablet) portraits blend towards height so content still fits.
                float aspect = (float)h / w;
                match = Mathf.InverseLerp(1.7f, 1.34f, aspect);
            }

            _scaler.matchWidthOrHeight = match;

            Vector2 min = new Vector2(safe.xMin / w, safe.yMin / h);
            Vector2 max = new Vector2(safe.xMax / w, safe.yMax / h);
            if (safe.width <= 0f || safe.height <= 0f)
            {
                min = Vector2.zero;
                max = Vector2.one;
            }

            for (int i = 0; i < _layers.Length; i++)
            {
                RectTransform rt = _layers[i];
                if (_layerSafe[i])
                {
                    rt.anchorMin = min;
                    rt.anchorMax = max;
                }
                else
                {
                    rt.anchorMin = Vector2.zero;
                    rt.anchorMax = Vector2.one;
                }

                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
            }

            if (!force)
            {
                _layoutEventFrame = Time.frameCount; // raised next frame, see Update
            }
        }

        /// <summary>
        /// Gives up <see cref="Instance"/> so a replacement root can be created in the same frame (the UI rebuild after a
        /// language change destroys this one; Destroy only happens at the end of the frame). No-op if not the instance.
        /// </summary>
        public void Retire()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _instance = null;
        }
    }
}

using System;
using System.Collections.Generic;
using BeamRunner.Core.Localization;
using UnityEngine;
using UnityEngine.UI;

namespace BeamRunner.UI
{
    /// <summary>
    /// Modal dialogs (dimmer + springy glass window). Requests are queued: a dialog opened while another is showing
    /// appears after it closes. Callbacks run as soon as a button is chosen. While a dialog is open, lower layers are
    /// made non-interactable so gamepad navigation can't escape it. Lives on the Dialogs layer of the <see cref="UIRoot"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Dialogs : MonoBehaviour
    {
        private sealed class Request
        {
            public string Title;
            public string Body;
            public IconId Icon;
            public Color? Accent;
            public string[] Options;
            public ButtonStyle[] Styles;
            public int CancelIndex;
            public bool DimmerCancels;
            public Action<int> OnChoice;
            public Action<RectTransform> BuildBody;
        }

        private readonly Queue<Request> _queue = new Queue<Request>();
        private readonly List<NeonButton> _active = new List<NeonButton>(4);
        private readonly Dictionary<ButtonStyle, List<NeonButton>> _pool = new Dictionary<ButtonStyle, List<NeonButton>>();
        private readonly List<CanvasGroup> _blocked = new List<CanvasGroup>(4);

        private RectTransform _root;
        private CanvasGroup _rootGroup;
        private Image _dimmer;
        private RectTransform _window;
        private UIFitHeight _fit;
        private CanvasGroup _windowGroup;
        private GlassPanel _panel;
        private RectTransform _iconWrap;
        private Image _iconRing;
        private Image _icon;
        private Text _title;
        private Text _body;
        private RectTransform _custom;
        private RectTransform _buttonsRow;
        private HorizontalLayoutGroup _rowLayout;
        private RectTransform _buttonsColumn;
        private Request _current;
        private bool _closing;
        private bool _activeIndexOffset;
        private Action _closedHandler;

        private static Dialogs Instance
        {
            get
            {
                UIRoot root = UIRoot.Ensure();
                return root != null ? root.Dialogs : null;
            }
        }

        /// <summary>True while a dialog is showing (or closing).</summary>
        public static bool IsOpen
        {
            get
            {
                UIRoot root = UIRoot.Instance;
                return root != null && root.Dialogs != null && root.Dialogs._current != null;
            }
        }

        /// <summary>The primary button of the open dialog (for gamepad focus), or null.</summary>
        public static GameObject CurrentDefaultSelection
        {
            get
            {
                UIRoot root = UIRoot.Instance;
                if (root == null || root.Dialogs == null || root.Dialogs._current == null || root.Dialogs._active.Count == 0)
                {
                    return null;
                }

                List<NeonButton> active = root.Dialogs._active;
                return active[active.Count - 1].gameObject;
            }
        }

        /// <summary>Two-button confirmation. <paramref name="onResult"/> receives true for confirm, false for cancel/back.</summary>
        public static void Confirm(string title, string body, string confirmLabel, string cancelLabel, Action<bool> onResult,
            bool danger = false, IconId icon = IconId.Info)
        {
            Enqueue(new Request
            {
                Title = title,
                Body = body,
                Icon = icon,
                Accent = danger ? UITheme.Danger : (Color?)null,
                Options = new[] { cancelLabel ?? Loc.T("common.cancel"), confirmLabel ?? Loc.T("common.ok") },
                Styles = new[] { ButtonStyle.Secondary, danger ? ButtonStyle.Danger : ButtonStyle.Primary },
                CancelIndex = 0,
                DimmerCancels = true,
                OnChoice = i =>
                {
                    if (onResult != null)
                    {
                        onResult(i == 1);
                    }
                },
            });
        }

        /// <summary>Single-button message.</summary>
        public static void Alert(string title, string body, string okLabel = null, Action onClose = null, IconId icon = IconId.Info)
        {
            Enqueue(new Request
            {
                Title = title,
                Body = body,
                Icon = icon,
                Options = new[] { okLabel ?? Loc.T("common.ok") },
                Styles = new[] { ButtonStyle.Primary },
                CancelIndex = 0,
                DimmerCancels = false,
                OnChoice = i =>
                {
                    if (onClose != null)
                    {
                        onClose();
                    }
                },
            });
        }

        /// <summary>
        /// Stacked options (first = primary). <paramref name="cancelIndex"/> is chosen on back/dimmer tap (-1 = not cancellable).
        /// </summary>
        public static void Choice(string title, string body, string[] options, Action<int> onChoice, int cancelIndex = -1,
            IconId icon = IconId.None)
        {
            Custom(title, body, null, options, onChoice, cancelIndex, icon);
        }

        /// <summary>
        /// Fully custom dialog: <paramref name="buildBody"/> fills a vertical-layout container under the body text
        /// (it is cleared when the dialog closes). Options are stacked, first = primary, unless
        /// <paramref name="optionStyles"/> gives an explicit style per option (e.g. equally prominent consent choices).
        /// </summary>
        public static void Custom(string title, string body, Action<RectTransform> buildBody, string[] options, Action<int> onChoice,
            int cancelIndex = -1, IconId icon = IconId.None, Color? accent = null, ButtonStyle[] optionStyles = null)
        {
            options = options ?? new string[0];
            var styles = new ButtonStyle[options.Length];
            for (int i = 0; i < styles.Length; i++)
            {
                if (optionStyles != null && i < optionStyles.Length)
                {
                    styles[i] = optionStyles[i];
                }
                else
                {
                    styles[i] = i == 0 ? ButtonStyle.Primary : (i == cancelIndex ? ButtonStyle.Ghost : ButtonStyle.Secondary);
                }
            }

            Enqueue(new Request
            {
                Title = title,
                Body = body,
                Icon = icon,
                Accent = accent,
                Options = options,
                Styles = styles,
                CancelIndex = cancelIndex,
                DimmerCancels = cancelIndex >= 0,
                OnChoice = onChoice,
                BuildBody = buildBody,
            });
        }

        /// <summary>
        /// Builds the dialog window and pools a few option buttons ahead of the first dialog (e.g. from
        /// <c>UIRoot.BuildQueue</c>), so the first Confirm/Alert/Custom doesn't build them while it opens.
        /// </summary>
        public static void Prewarm()
        {
            UIRoot root = UIRoot.Instance;
            Dialogs d = root != null ? root.Dialogs : null;
            if (d == null)
            {
                return;
            }

            d.EnsureView();
            d.PrewarmButtons(ButtonStyle.Secondary, 2);
            d.PrewarmButtons(ButtonStyle.Primary, 1);
        }

        /// <summary>Routes a back press to the open dialog. Returns true if a dialog consumed it.</summary>
        public static bool HandleBack()
        {
            UIRoot root = UIRoot.Instance;
            Dialogs d = root != null ? root.Dialogs : null;
            if (d == null || d._current == null)
            {
                return false;
            }

            if (!d._closing && d._current.CancelIndex >= 0)
            {
                UIFactory.PlayClickFeedback();
                d.Close(d._current.CancelIndex);
            }

            return true;
        }

        /// <summary>Closes the open dialog (choosing its cancel option, or nothing) and drops queued ones.</summary>
        public static void CloseAll()
        {
            UIRoot root = UIRoot.Instance;
            Dialogs d = root != null ? root.Dialogs : null;
            if (d == null)
            {
                return;
            }

            d._queue.Clear();
            if (d._current != null && !d._closing)
            {
                d.Close(d._current.CancelIndex);
            }
        }

        private static void Enqueue(Request r)
        {
            Dialogs d = Instance;
            if (d == null)
            {
                return;
            }

            d._queue.Enqueue(r);
            if (d._current == null)
            {
                d.OpenNext();
            }
        }

        // ------------------------------------------------------------------ view

        private void OpenNext()
        {
            if (_current != null || _queue.Count == 0)
            {
                return;
            }

            EnsureView();
            Request r = _queue.Dequeue();
            _current = r;
            _closing = false;

            RectTransform layer = (RectTransform)transform;
            float width = Mathf.Min(940f, Mathf.Max(600f, layer.rect.width - 96f));
            _window.sizeDelta = new Vector2(width, _window.sizeDelta.y);

            _panel.SetAccent(r.Accent);
            _iconWrap.gameObject.SetActive(r.Icon != IconId.None);
            UIFactory.SetIcon(_icon, r.Icon);
            if (r.Accent.HasValue)
            {
                UIAccentFollower.Detach(_iconRing);
                UIAccentFollower.Detach(_icon);
                _iconRing.color = r.Accent.Value;
                _icon.color = r.Accent.Value;
            }
            else
            {
                UIAccentFollower.Attach(_iconRing, AccentSlot.Primary);
                UIAccentFollower.Attach(_icon, AccentSlot.Primary);
            }

            _title.text = r.Title ?? string.Empty;
            _title.gameObject.SetActive(!string.IsNullOrEmpty(r.Title));
            _body.text = r.Body ?? string.Empty;
            _body.gameObject.SetActive(!string.IsNullOrEmpty(r.Body));

            ClearCustom();
            _custom.gameObject.SetActive(r.BuildBody != null);
            if (r.BuildBody != null)
            {
                try
                {
                    r.BuildBody(_custom);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }

            SetupButtons(r);
            BlockLowerLayers(true);

            _root.gameObject.SetActive(true);
            _root.SetAsLastSibling();
            LayoutRebuilder.ForceRebuildLayoutImmediate(_window);
            _fit.Apply(); // fit the freshly measured window before its first frame

            UITween.Fade(_rootGroup, 0f, 1f, 0.2f).SetEase(UIEase.OutQuad);
            UITween.Fade(_windowGroup, 0f, 1f, 0.22f).SetEase(UIEase.OutCubic);
            UITween.Scale(_window, 0.82f, 1f, 0.42f).SetEase(UIEase.OutBack);
            UITween.Move(_window, new Vector2(0f, -40f), Vector2.zero, 0.4f).SetEase(UIEase.OutCubic);
            if (_iconWrap.gameObject.activeSelf)
            {
                UITween.Scale(_iconWrap, 0.2f, 1f, 0.5f).SetDelay(0.08f).SetEase(UIEase.OutBack);
            }

            UIFactory.StaggerIn(_buttonsRow.gameObject.activeSelf ? _buttonsRow : _buttonsColumn, 0.12f, 0.06f);

            UIStack stack = UIRoot.Instance != null ? UIRoot.Instance.Stack : null;
            if (stack != null && stack.IsNavigating)
            {
                UnityEngine.EventSystems.EventSystem es = UnityEngine.EventSystems.EventSystem.current;
                if (es != null && _active.Count > 0)
                {
                    es.SetSelectedGameObject(_active[_active.Count - 1].gameObject);
                }
            }
        }

        private void SetupButtons(Request r)
        {
            for (int i = 0; i < _active.Count; i++)
            {
                NeonButton b = _active[i];
                b.gameObject.SetActive(false);
                b.transform.SetParent(_window, false);
                GetPool(b.Style).Add(b);
            }

            _active.Clear();
            _activeIndexOffset = false;
            bool horizontal = r.Options.Length == 2 && ShortLabels(r.Options);
            _buttonsRow.gameObject.SetActive(horizontal);
            _buttonsColumn.gameObject.SetActive(!horizontal && r.Options.Length > 0);
            RectTransform parent = horizontal ? _buttonsRow : _buttonsColumn;

            // Column order: primary first; row order: as given (cancel left, confirm right).
            for (int i = 0; i < r.Options.Length; i++)
            {
                ButtonStyle style = r.Styles != null && i < r.Styles.Length ? r.Styles[i] : ButtonStyle.Secondary;
                NeonButton b = Take(style);
                b.transform.SetParent(parent, false);
                b.transform.SetAsLastSibling();
                b.gameObject.SetActive(true);
                b.SetLabel(r.Options[i]);
                LayoutElement le = b.GetComponent<LayoutElement>();
                if (le != null)
                {
                    le.flexibleWidth = 1f;
                    le.minWidth = 0f;
                    le.preferredWidth = horizontal ? 0f : le.preferredWidth;
                }

                _active.Add(b);
            }

            if (horizontal)
            {
                // Default focus = the confirm (last) button.
                return;
            }

            // In a column the first option is primary: make it the default focus by moving it to the end of _active.
            if (_active.Count > 1)
            {
                NeonButton first = _active[0];
                _active.RemoveAt(0);
                _active.Add(first);
                _activeIndexOffset = true;
            }
        }

        private int IndexOf(NeonButton b)
        {
            int i = _active.IndexOf(b);
            if (i < 0)
            {
                return -1;
            }

            if (_activeIndexOffset)
            {
                // _active = [1, 2, ..., n-1, 0]
                return i == _active.Count - 1 ? 0 : i + 1;
            }

            return i;
        }

        private static bool ShortLabels(string[] options)
        {
            for (int i = 0; i < options.Length; i++)
            {
                if (options[i] != null && options[i].Length > 12)
                {
                    return false;
                }
            }

            return true;
        }

        private NeonButton Take(ButtonStyle style)
        {
            List<NeonButton> pool = GetPool(style);
            while (pool.Count > 0)
            {
                NeonButton b = pool[pool.Count - 1];
                pool.RemoveAt(pool.Count - 1);
                if (b != null)
                {
                    return b;
                }
            }

            return CreateOption(style);
        }

        private NeonButton CreateOption(ButtonStyle style)
        {
            NeonButton created = UIFactory.Button(_window, string.Empty, style, null, IconId.None, null, UITheme.ButtonHeight, 0f);
            created.OnClick(() => OnButton(created));
            return created;
        }

        private void PrewarmButtons(ButtonStyle style, int count)
        {
            List<NeonButton> pool = GetPool(style);
            int active = 0;
            for (int i = 0; i < _active.Count; i++)
            {
                if (_active[i] != null && _active[i].Style == style)
                {
                    active++;
                }
            }

            for (int i = pool.Count + active; i < count; i++)
            {
                NeonButton b = CreateOption(style);
                b.gameObject.SetActive(false);
                pool.Add(b);
            }
        }

        private List<NeonButton> GetPool(ButtonStyle style)
        {
            if (!_pool.TryGetValue(style, out List<NeonButton> list))
            {
                list = new List<NeonButton>(2);
                _pool[style] = list;
            }

            return list;
        }

        private void OnButton(NeonButton b)
        {
            if (_current == null || _closing)
            {
                return;
            }

            int index = IndexOf(b);
            if (index >= 0)
            {
                Close(index);
            }
        }

        private void OnDimmer()
        {
            if (_current != null && !_closing && _current.DimmerCancels && _current.CancelIndex >= 0)
            {
                UIFactory.PlayClickFeedback();
                Close(_current.CancelIndex);
            }
        }

        private void Close(int choice)
        {
            if (_current == null || _closing)
            {
                return;
            }

            _closing = true;
            Request r = _current;
            UITween.Fade(_windowGroup, 0f, 0.16f).SetEase(UIEase.InQuad);
            UITween.Scale(_window, 0.92f, 0.16f).SetEase(UIEase.InQuad);
            UITween.Fade(_rootGroup, 0f, 0.2f).SetEase(UIEase.InQuad).OnComplete(_closedHandler);

            if (choice >= 0 && r.OnChoice != null)
            {
                try
                {
                    r.OnChoice(choice);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }

        private void HandleClosed()
        {
            _root.gameObject.SetActive(false);
            ClearCustom();
            _current = null;
            _closing = false;
            if (_queue.Count > 0)
            {
                OpenNext();
            }
            else
            {
                BlockLowerLayers(false);
            }
        }

        private void ClearCustom()
        {
            if (_custom == null)
            {
                return;
            }

            for (int i = _custom.childCount - 1; i >= 0; i--)
            {
                GameObject child = _custom.GetChild(i).gameObject;
                child.SetActive(false);
                Destroy(child);
            }
        }

        private void BlockLowerLayers(bool block)
        {
            UIRoot root = UIRoot.Instance;
            if (root == null)
            {
                return;
            }

            if (block)
            {
                if (_blocked.Count > 0)
                {
                    return;
                }

                AddBlock(root.GetLayer(UILayer.Screens));
                AddBlock(root.GetLayer(UILayer.HUD));
                AddBlock(root.GetLayer(UILayer.Popups));
            }
            else
            {
                for (int i = 0; i < _blocked.Count; i++)
                {
                    if (_blocked[i] != null)
                    {
                        _blocked[i].interactable = true;
                    }
                }

                _blocked.Clear();
            }
        }

        private void AddBlock(RectTransform layer)
        {
            if (layer == null)
            {
                return;
            }

            CanvasGroup g = layer.GetComponent<CanvasGroup>();
            if (g == null)
            {
                g = layer.gameObject.AddComponent<CanvasGroup>();
            }

            g.interactable = false;
            _blocked.Add(g);
        }

        private void EnsureView()
        {
            if (_root != null)
            {
                return;
            }

            _closedHandler = HandleClosed;
            _root = UIFactory.CreateFill(transform, "Dialog");
            _rootGroup = _root.gameObject.AddComponent<CanvasGroup>();

            _dimmer = UIFactory.CreateImage(_root, null, UITheme.Dimmer, "Dimmer", true);
            UIFactory.Stretch(_dimmer.rectTransform, -4f, -4f, -4f, -4f);
            Button dimButton = _dimmer.gameObject.AddComponent<Button>();
            dimButton.transition = Selectable.Transition.None;
            Navigation nav = dimButton.navigation;
            nav.mode = Navigation.Mode.None;
            dimButton.navigation = nav;
            dimButton.onClick.AddListener(OnDimmer);

            // The window sits in a full-size host that UIFitHeight shrinks when the window is taller than the screen
            // (e.g. the consent dialog, ~1060 units, on a 1080-unit landscape canvas). The dimmer stays full-bleed.
            RectTransform host = UIFactory.CreateFill(_root, "WindowHost");
            _window = UIFactory.CreateRect(host, "Window");
            _window.sizeDelta = new Vector2(900f, 600f);
            _windowGroup = _window.gameObject.AddComponent<CanvasGroup>();
            _panel = _window.gameObject.AddComponent<GlassPanel>();
            _panel.Build(null, UITheme.RadiusXl, 0f, true, true, true, false);
            VerticalLayoutGroup v = UIFactory.VStack(_window, UITheme.SpaceMd, UIFactory.Padding(56, 56, 52, 52), TextAnchor.UpperCenter, true);
            v.childForceExpandWidth = true;
            UIFactory.FitContent(_window, false, true);

            _iconWrap = UIFactory.CreateRect(_window, "IconWrap");
            UIFactory.SetLayout(_iconWrap, -1f, 132f);
            RectTransform badge = UIFactory.CreateRect(_iconWrap, "Badge");
            badge.sizeDelta = new Vector2(132f, 132f);
            Image glow = UIFactory.CreateImage(badge, UISprites.Glow, UITheme.Accent, "Glow");
            UIFactory.Stretch(glow.rectTransform, -40f, -40f, -40f, -40f);
            UIAccentFollower.Attach(glow, AccentSlot.Primary, 0.35f);
            Image disc = UIFactory.CreateImage(badge, UISprites.Circle, UITheme.PanelGlassLight, "Disc");
            UIFactory.Stretch(disc.rectTransform);
            _iconRing = UIFactory.CreateImage(badge, UISprites.Ring(12), UITheme.Accent, "Ring");
            UIFactory.Stretch(_iconRing.rectTransform);
            _icon = UIFactory.Icon(badge, IconId.Info, 64f, UITheme.Accent);

            _title = UIFactory.Label(_window, string.Empty, UITheme.TextH3, UITheme.TextPrimary, TextAnchor.MiddleCenter, UIFont.Title, TextGlow.Neon);
            _body = UIFactory.Label(_window, string.Empty, UITheme.TextBody, UITheme.TextSecondary, TextAnchor.MiddleCenter, UIFont.Body);

            _custom = UIFactory.CreateRect(_window, "Custom");
            UIFactory.VStack(_custom, UITheme.SpaceSm, null, TextAnchor.UpperCenter, true);

            UIFactory.Spacer(_window, 8f);

            _buttonsRow = UIFactory.CreateRect(_window, "ButtonsRow");
            _rowLayout = UIFactory.HStack(_buttonsRow, UITheme.SpaceMd, null, TextAnchor.MiddleCenter, true);
            _rowLayout.childForceExpandWidth = true;
            _buttonsColumn = UIFactory.CreateRect(_window, "ButtonsColumn");
            UIFactory.VStack(_buttonsColumn, UITheme.SpaceSm, null, TextAnchor.UpperCenter, true);
            _fit = UIFitHeight.Attach(host, _window, 40f);

            _root.gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            BlockLowerLayers(false);
        }
    }
}

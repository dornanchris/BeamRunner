using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BeamRunner.UI
{
    /// <summary>Visual style of a <see cref="NeonButton"/>.</summary>
    public enum ButtonStyle
    {
        /// <summary>Big gradient pill with glow — the main call to action.</summary>
        Primary = 0,

        /// <summary>Dark glass pill with a neon outline.</summary>
        Secondary = 1,

        /// <summary>Text only; a faint plate appears on hover/press.</summary>
        Ghost = 2,

        /// <summary>Red gradient pill for destructive actions.</summary>
        Danger = 3,
    }

    /// <summary>
    /// Kit button: a <see cref="Button"/> (keyboard/gamepad navigation and submit included) with procedural visuals,
    /// press squash / springy release, hover glow, disabled state, optional icon and sub-label (e.g. a price), and
    /// global click feedback via <see cref="UIFactory.OnClickSound"/> / <see cref="UIFactory.OnHaptic"/>.
    /// Build with <see cref="UIFactory.Button"/> or <see cref="UIFactory.IconButton"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NeonButton : Button
    {
        private RectTransform _visual;
        private CanvasGroup _visualGroup;
        private Image _glow;
        private Image _fill;
        private Image _sheen;
        private Image _flash;
        private Image _frame;
        private Image _halo;
        private Text _label;
        private Text _subLabel;
        private Image _icon;
        private RectTransform _content;
        private LayoutElement _layout;
        private ButtonStyle _style;
        private bool _round;
        private bool _built;
        private bool _navSelected;
        private float _height;
        private float _minWidth;
        private float _glowRest;
        private SelectionState _lastState = SelectionState.Normal;
        private int _haloTween;
        private Color _labelColor;

        /// <summary>Play <see cref="UIFactory.OnClickSound"/> on click.</summary>
        public bool PlaySound = true;

        /// <summary>Play <see cref="UIFactory.OnHaptic"/> on click.</summary>
        public bool PlayHaptic = true;

        public ButtonStyle Style
        {
            get { return _style; }
        }

        /// <summary>The animated visual root (scaled on press); add custom decoration here.</summary>
        public RectTransform Visual
        {
            get { return _visual; }
        }

        public Text Label
        {
            get { return _label; }
        }

        public Text SubLabel
        {
            get { return _subLabel; }
        }

        public Image IconImage
        {
            get { return _icon; }
        }

        public RectTransform Rect
        {
            get { return (RectTransform)transform; }
        }

        /// <summary>Adds a click handler (fluent).</summary>
        public NeonButton OnClick(Action action)
        {
            if (action != null)
            {
                onClick.AddListener(action.Invoke);
            }

            return this;
        }

        /// <summary>Enables/disables the button (animated disabled look).</summary>
        public void SetInteractable(bool value)
        {
            interactable = value;
        }

        public void SetLabel(string text)
        {
            if (_label == null)
            {
                return;
            }

            _label.text = FormatLabel(text);
            _label.gameObject.SetActive(!string.IsNullOrEmpty(text));
            Remeasure();
        }

        public void SetSubLabel(string text)
        {
            if (_subLabel == null)
            {
                return;
            }

            _subLabel.text = text ?? string.Empty;
            _subLabel.gameObject.SetActive(!string.IsNullOrEmpty(text));
            Remeasure();
        }

        public void SetIcon(IconId id)
        {
            if (_icon == null)
            {
                return;
            }

            _icon.sprite = UIIcons.Get(id);
            _icon.gameObject.SetActive(id != IconId.None);
            Remeasure();
        }

        /// <summary>Overrides the fill gradient (Primary/Danger) or frame colour (Secondary/round) — stops following the accent.</summary>
        public void SetColors(Color from, Color to)
        {
            if (_style == ButtonStyle.Primary || _style == ButtonStyle.Danger)
            {
                if (_fill != null)
                {
                    UIGradient.Apply(_fill, from, to, GradientDirection.Horizontal);
                }

                if (_glow != null)
                {
                    UIGradient.Apply(_glow, UITheme.Alpha(from, 0.9f), UITheme.Alpha(to, 0.9f), GradientDirection.Horizontal);
                }
            }
            else
            {
                if (_frame != null)
                {
                    UIAccentFollower.Detach(_frame);
                    _frame.color = to;
                }

                if (_glow != null && _round)
                {
                    UIAccentFollower.Detach(_glow);
                    _glow.color = UITheme.Alpha(to, 0.45f);
                }
            }
        }

        /// <summary>Loops a soft halo around the button to draw attention (e.g. "Claim reward").</summary>
        public void SetAttention(bool on)
        {
            if (!_built)
            {
                return;
            }

            if (on)
            {
                if (_halo == null)
                {
                    _halo = UIFactory.CreateImage(_visual, null, UITheme.Accent, "Halo");
                    _halo.transform.SetAsFirstSibling();
                    float r = _round ? _height * 0.5f : _height * 0.5f;
                    UISprites.ApplySoft(_halo, r, 44f);
                    UIAccentFollower.Attach(_halo, AccentSlot.Primary, 0.9f);
                }

                _halo.gameObject.SetActive(true);
                UITween.KillId(_haloTween);
                _haloTween = UIFactory.Breathe(_halo, 0.1f, 0.85f, 1.3f);
            }
            else if (_halo != null)
            {
                UITween.KillId(_haloTween);
                _haloTween = 0;
                _halo.gameObject.SetActive(false);
            }
        }

        // ------------------------------------------------------------------ construction (called by UIFactory)

        internal void Build(ButtonStyle style, string label, IconId icon, string subLabel, float height, float minWidth)
        {
            _style = style;
            _round = false;
            _height = height;
            _minWidth = minWidth;
            transition = Transition.None;

            RectTransform root = Rect;
            _visual = UIFactory.CreateFill(root, "Visual");
            _visualGroup = _visual.gameObject.AddComponent<CanvasGroup>();
            _visualGroup.blocksRaycasts = false;
            float radius = height * 0.5f;

            switch (style)
            {
                case ButtonStyle.Primary:
                case ButtonStyle.Danger:
                {
                    bool danger = style == ButtonStyle.Danger;
                    _glow = UIFactory.CreateImage(_visual, null, Color.white, "Glow");
                    UISprites.ApplySoft(_glow, radius, 34f, new Vector2(0f, -6f));
                    _fill = UIFactory.PillImage(_visual, Color.white, "Fill");
                    if (danger)
                    {
                        UIGradient.Apply(_glow, UITheme.Alpha(UITheme.Danger, 0.8f), UITheme.Alpha(UITheme.DangerAlt, 0.8f), GradientDirection.Horizontal);
                        UIGradient.Apply(_fill, UITheme.Danger, UITheme.DangerAlt, GradientDirection.Horizontal);
                    }
                    else
                    {
                        UIGradient.ApplyAccent(_glow, GradientDirection.Horizontal, 0.85f);
                        UIGradient.ApplyAccent(_fill, GradientDirection.Horizontal);
                    }

                    _sheen = UIFactory.PillImage(_visual, Color.white, "Sheen");
                    UIFactory.Stretch(_sheen.rectTransform, 6f, 5f, 6f, height * 0.46f);
                    UIGradient.Apply(_sheen, new Color(1f, 1f, 1f, 0.42f), new Color(1f, 1f, 1f, 0f), GradientDirection.Vertical);
                    _glowRest = 0.7f;
                    _labelColor = danger ? UITheme.TextPrimary : UITheme.TextOnAccent;
                    break;
                }

                case ButtonStyle.Secondary:
                {
                    _fill = UIFactory.PillImage(_visual, UITheme.PanelGlass, "Fill");
                    UIGradient.Apply(_fill, Color.white, new Color(0.8f, 0.8f, 0.85f, 1f), GradientDirection.Vertical);
                    _frame = UIFactory.CreateImage(_visual, null, UITheme.Accent, "Frame");
                    UISprites.ApplyFrame(_frame, radius, 4f, 22f);
                    UIAccentFollower.Attach(_frame, AccentSlot.Primary);
                    _glow = _frame;
                    _glowRest = 0.8f;
                    _labelColor = UITheme.TextPrimary;
                    break;
                }

                default:
                {
                    _fill = UIFactory.PillImage(_visual, UITheme.PanelGlassLight, "Plate");
                    _fill.canvasRenderer.SetAlpha(0f);
                    _glow = _fill;
                    _glowRest = 0f;
                    _labelColor = UITheme.TextSecondary;
                    break;
                }
            }

            _flash = UIFactory.PillImage(_visual, Color.white, "Flash");
            _flash.canvasRenderer.SetAlpha(0f);

            // Content: [icon] [label / sub-label]
            _content = UIFactory.CreateFill(_visual, "Content");
            UIFactory.Stretch(_content, radius * 0.6f, 0f, radius * 0.6f, 0f);
            HorizontalLayoutGroup row = UIFactory.HStack(_content, 18f, null, TextAnchor.MiddleCenter, true);
            row.childAlignment = TextAnchor.MiddleCenter;

            float iconSize = Mathf.Round(height * 0.42f);
            _icon = UIFactory.Icon(_content, icon, iconSize, style == ButtonStyle.Secondary ? UITheme.Accent : _labelColor);
            if (style == ButtonStyle.Secondary)
            {
                UIAccentFollower.Attach(_icon, AccentSlot.Primary);
            }

            _icon.gameObject.SetActive(icon != IconId.None);

            RectTransform texts = UIFactory.CreateRect(_content, "Texts");
            VerticalLayoutGroup col = UIFactory.VStack(texts, -4f, null, TextAnchor.MiddleCenter, true);
            col.childForceExpandWidth = false;
            col.childControlWidth = true;

            bool display = style != ButtonStyle.Ghost;
            int labelSize = style == ButtonStyle.Primary ? 46 : (style == ButtonStyle.Ghost ? 42 : 40);
            _label = UIFactory.LabelLine(texts, FormatLabel(label), labelSize, _labelColor, TextAnchor.MiddleCenter,
                display ? UIFont.Title : UIFont.Bold);
            _label.gameObject.SetActive(!string.IsNullOrEmpty(label));
            if (style == ButtonStyle.Danger || style == ButtonStyle.Secondary)
            {
                UIFactory.AddGlow(_label, TextGlow.Shadow);
            }

            _subLabel = UIFactory.LabelLine(texts, subLabel ?? string.Empty, 30, UITheme.Alpha(_labelColor, 0.78f), TextAnchor.MiddleCenter, UIFont.Bold);
            _subLabel.gameObject.SetActive(!string.IsNullOrEmpty(subLabel));

            FinishBuild();
            Remeasure();
        }

        internal void BuildRound(ButtonStyle style, IconId icon, float size)
        {
            _style = style;
            _round = true;
            _height = size;
            _minWidth = size;
            transition = Transition.None;

            RectTransform root = Rect;
            root.sizeDelta = new Vector2(size, size);
            _visual = UIFactory.CreateFill(root, "Visual");
            _visualGroup = _visual.gameObject.AddComponent<CanvasGroup>();
            _visualGroup.blocksRaycasts = false;

            bool filled = style == ButtonStyle.Primary || style == ButtonStyle.Danger;
            _glow = UIFactory.CreateImage(_visual, UISprites.Glow, UITheme.Accent, "Glow");
            UIFactory.Stretch(_glow.rectTransform, -size * 0.32f, -size * 0.32f, -size * 0.32f, -size * 0.32f);
            if (style == ButtonStyle.Danger)
            {
                _glow.color = UITheme.Alpha(UITheme.Danger, 0.55f);
            }
            else
            {
                UIAccentFollower.Attach(_glow, filled ? AccentSlot.Secondary : AccentSlot.Primary, 0.42f);
            }

            _fill = UIFactory.CreateImage(_visual, UISprites.Circle, filled ? Color.white : UITheme.PanelGlass, "Fill");
            UIFactory.Stretch(_fill.rectTransform);
            if (style == ButtonStyle.Danger)
            {
                UIGradient.Apply(_fill, UITheme.Danger, UITheme.DangerAlt, GradientDirection.Diagonal);
            }
            else if (filled)
            {
                UIGradient.ApplyAccent(_fill, GradientDirection.Diagonal);
            }

            if (!filled)
            {
                _frame = UIFactory.CreateImage(_visual, UISprites.Ring(10), UITheme.Accent, "Ring");
                UIFactory.Stretch(_frame.rectTransform);
                if (style == ButtonStyle.Ghost)
                {
                    _frame.color = UITheme.PanelBorder;
                }
                else
                {
                    UIAccentFollower.Attach(_frame, AccentSlot.Primary);
                }
            }

            _sheen = UIFactory.CreateImage(_visual, UISprites.Circle, Color.white, "Sheen");
            UIFactory.Stretch(_sheen.rectTransform, size * 0.08f, size * 0.06f, size * 0.08f, size * 0.5f);
            UIGradient.Apply(_sheen, new Color(1f, 1f, 1f, filled ? 0.35f : 0.12f), new Color(1f, 1f, 1f, 0f), GradientDirection.Vertical);

            _flash = UIFactory.CreateImage(_visual, UISprites.Circle, Color.white, "Flash");
            UIFactory.Stretch(_flash.rectTransform);
            _flash.canvasRenderer.SetAlpha(0f);

            _labelColor = filled ? (style == ButtonStyle.Danger ? UITheme.TextPrimary : UITheme.TextOnAccent) : UITheme.TextPrimary;
            _icon = UIFactory.Icon(_visual, icon, Mathf.Round(size * 0.46f), _labelColor);
            _glowRest = style == ButtonStyle.Ghost ? 0f : 0.75f;

            FinishBuild();
            _layout.preferredWidth = size;
            _layout.preferredHeight = size;
            _layout.minWidth = size;
            _layout.minHeight = size;
        }

        private void FinishBuild()
        {
            _layout = UIFactory.SetLayout(this, -1f, _height);
            onClick.AddListener(HandleClick);
            _built = true;
            if (_glow != null && _glow != _fill)
            {
                _glow.canvasRenderer.SetAlpha(_glowRest);
            }

            DoStateTransition(currentSelectionState, true);
        }

        private void Remeasure()
        {
            if (!_built || _round || _layout == null)
            {
                return;
            }

            // Design-size widths (UITextFit reports them), so a label that had to shrink never shrinks the button.
            float textW = 0f;
            if (_label != null && _label.gameObject.activeSelf)
            {
                textW = LayoutUtility.GetPreferredWidth(_label.rectTransform);
            }

            if (_subLabel != null && _subLabel.gameObject.activeSelf)
            {
                textW = Mathf.Max(textW, LayoutUtility.GetPreferredWidth(_subLabel.rectTransform));
            }

            float w = textW + _height * 1.2f;
            if (_icon != null && _icon.gameObject.activeSelf)
            {
                w += _icon.rectTransform.sizeDelta.x + 18f;
            }

            w = Mathf.Ceil(Mathf.Max(_minWidth, w));
            _layout.preferredWidth = w;
            _layout.minWidth = Mathf.Min(w, _minWidth > 0f ? _minWidth : w);
            RectTransform rt = Rect;
            if (Mathf.Approximately(rt.anchorMin.x, rt.anchorMax.x))
            {
                // Free-standing button: grow with the label, but never past the parent (the label shrinks instead).
                RectTransform parent = rt.parent as RectTransform;
                float room = parent != null ? parent.rect.width - 2f * UITheme.SpaceLg : 0f;
                if (room > _minWidth && w > room)
                {
                    w = Mathf.Floor(room);
                }

                rt.sizeDelta = new Vector2(w, _height);
            }
        }

        private static string FormatLabel(string text)
        {
            // Shown as given: display-style labels come upper-case from the string tables (no runtime case changes).
            return text ?? string.Empty;
        }

        private void HandleClick()
        {
            UIFactory.PlayClickFeedback(PlaySound, PlayHaptic);
            if (_flash != null)
            {
                _flash.canvasRenderer.SetAlpha(_style == ButtonStyle.Ghost ? 0.12f : 0.45f);
                UITween.RendererAlpha(_flash, 0f, 0.35f).SetEase(UIEase.OutCubic);
            }
        }

        // ------------------------------------------------------------------ state visuals

        public override void OnSelect(BaseEventData eventData)
        {
            _navSelected = !(eventData is PointerEventData);
            base.OnSelect(eventData);
        }

        public override void OnDeselect(BaseEventData eventData)
        {
            _navSelected = false;
            base.OnDeselect(eventData);
        }

        protected override void DoStateTransition(SelectionState state, bool instant)
        {
            base.DoStateTransition(state, instant);
            if (!_built || _visual == null)
            {
                return;
            }

            if (state == SelectionState.Selected && !_navSelected)
            {
                state = SelectionState.Normal;
            }

            Vector3 scale = Vector3.one;
            float glow = _glowRest;
            float alpha = 1f;
            float duration = 0.22f;
            UIEase ease = UIEase.OutCubic;
            switch (state)
            {
                case SelectionState.Pressed:
                    scale = new Vector3(0.955f, 0.9f, 1f);
                    glow = 1f;
                    duration = 0.08f;
                    ease = UIEase.OutQuad;
                    break;
                case SelectionState.Highlighted:
                case SelectionState.Selected:
                    // Focus / hover swell (UIFocusRing uses the same scale); reduced motion keeps only the glow.
                    if (!UITheme.ReducedMotion)
                    {
                        scale = new Vector3(UIFocusRing.SwellScale, UIFocusRing.SwellScale, 1f);
                    }

                    glow = _style == ButtonStyle.Ghost ? 0.9f : 1f;
                    if (_lastState == SelectionState.Pressed)
                    {
                        duration = 0.45f;
                        ease = UIEase.OutSpring;
                    }

                    break;
                case SelectionState.Disabled:
                    alpha = 0.38f;
                    glow = 0f;
                    break;
                default:
                    if (_lastState == SelectionState.Pressed)
                    {
                        duration = 0.45f;
                        ease = UIEase.OutSpring;
                    }

                    break;
            }

            _lastState = state;
            if (instant || !Application.isPlaying)
            {
                UITween.Kill(_visual);
                _visual.localScale = scale;
                _visualGroup.alpha = alpha;
                if (_glow != null)
                {
                    UITween.Kill(_glow);
                    _glow.canvasRenderer.SetAlpha(glow);
                }

                return;
            }

            UITween.Scale(_visual, scale, duration).SetEase(ease);
            UITween.Fade(_visualGroup, alpha, 0.18f).SetEase(UIEase.OutQuad);
            if (_glow != null)
            {
                UITween.RendererAlpha(_glow, glow, state == SelectionState.Pressed ? 0.06f : 0.25f).SetEase(UIEase.OutQuad);
            }
        }
    }
}

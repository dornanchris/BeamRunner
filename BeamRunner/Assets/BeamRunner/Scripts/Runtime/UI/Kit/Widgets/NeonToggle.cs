using System;
using UnityEngine;
using UnityEngine.UI;

namespace BeamRunner.UI
{
    /// <summary>
    /// Pill switch with an animated knob (springy slide, accent track + glow when on). Wraps a standard
    /// <see cref="Toggle"/> for navigation/submit (Submit toggles it). A <see cref="UIFocusRing"/> frames and swells it while
    /// focused by keyboard/gamepad. Inside a scroll list a drag that starts on it scrolls the list (the toggle has no drag
    /// handler, so drags bubble to the ScrollRect, which cancels the click). Build with <see cref="UIFactory.Toggle"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NeonToggle : MonoBehaviour
    {
        public const float Width = 136f;
        public const float Height = 76f;
        private const float KnobSize = 58f;

        private Toggle _toggle;
        private Image _trackOn;
        private Image _glow;
        private RectTransform _knob;
        private Image _knobImage;
        private Action<bool> _onChanged;

        /// <summary>The underlying uGUI toggle.</summary>
        public Toggle Toggle
        {
            get { return _toggle; }
        }

        public RectTransform Rect
        {
            get { return (RectTransform)transform; }
        }

        public bool IsOn
        {
            get { return _toggle.isOn; }
            set { _toggle.isOn = value; }
        }

        /// <summary>Sets the state (animated or not) without invoking the change callback.</summary>
        public void SetIsOnWithoutNotify(bool value, bool animate = false)
        {
            _toggle.SetIsOnWithoutNotify(value);
            Refresh(animate);
        }

        internal void Build(bool value, Action<bool> onChanged)
        {
            RectTransform root = Rect;
            root.sizeDelta = new Vector2(Width, Height);
            UIFactory.SetLayout(this, Width, Height);
            _onChanged = onChanged;

            UIRaycastTarget hit = root.gameObject.AddComponent<UIRaycastTarget>();

            _glow = UIFactory.CreateImage(root, null, UITheme.Accent, "Glow");
            UISprites.ApplySoft(_glow, Height * 0.5f, 24f);
            UIAccentFollower.Attach(_glow, AccentSlot.Primary, 0.5f);

            Image trackOff = UIFactory.PillImage(root, UITheme.PanelGlassLight, "Track");
            Image border = UIFactory.CreateImage(root, null, UITheme.PanelBorder, "Border");
            UIFactory.Stretch(border.rectTransform);
            UISprites.ApplyOutline(border, Height * 0.5f, 2f);
            _trackOn = UIFactory.PillImage(root, Color.white, "TrackOn");
            UIGradient.ApplyAccent(_trackOn, GradientDirection.Horizontal);

            _knob = UIFactory.CreateRect(root, "Knob");
            _knob.sizeDelta = new Vector2(KnobSize, KnobSize);
            Image knobShadow = UIFactory.CreateImage(_knob, UISprites.Glow, new Color(0f, 0f, 0f, 0.5f), "Shadow");
            UIFactory.Stretch(knobShadow.rectTransform, -14f, -10f, -14f, -18f);
            _knobImage = UIFactory.CreateImage(_knob, UISprites.Circle, UITheme.TextPrimary, "Body");
            UIFactory.Stretch(_knobImage.rectTransform);

            _toggle = root.gameObject.AddComponent<Toggle>();
            _toggle.transition = Selectable.Transition.None;
            _toggle.toggleTransition = UnityEngine.UI.Toggle.ToggleTransition.None;
            _toggle.graphic = null;
            _toggle.targetGraphic = hit;
            _toggle.SetIsOnWithoutNotify(value);
            _toggle.onValueChanged.AddListener(HandleChanged);
            UIFocusRing.Attach(_toggle, root, Height * 0.5f, 6f, root);
            Refresh(false);
        }

        private void HandleChanged(bool on)
        {
            UIFactory.PlayClickFeedback();
            Refresh(true);
            if (_onChanged != null)
            {
                try
                {
                    _onChanged(on);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }

        private void Refresh(bool animate)
        {
            if (_knob == null)
            {
                return;
            }

            bool on = _toggle.isOn;
            float x = (Width * 0.5f - Height * 0.5f) * (on ? 1f : -1f);
            float onAlpha = on ? 1f : 0f;
            Color knobColor = on ? UITheme.TextPrimary : UITheme.TextSecondary;
            if (!animate || !Application.isPlaying)
            {
                UITween.Kill(_knob);
                _knob.anchoredPosition = new Vector2(x, 0f);
                _trackOn.canvasRenderer.SetAlpha(onAlpha);
                _glow.canvasRenderer.SetAlpha(onAlpha);
                _knobImage.color = knobColor;
                return;
            }

            UITween.Move(_knob, new Vector2(x, 0f), 0.34f).SetEase(UIEase.OutBack);
            UITween.Scale(_knob, new Vector3(1.18f, 0.88f, 1f), 0.08f).SetEase(UIEase.OutQuad)
                .OnComplete(ReleaseKnob);
            UITween.RendererAlpha(_trackOn, onAlpha, 0.22f);
            UITween.RendererAlpha(_glow, onAlpha, 0.3f);
            UITween.Color(_knobImage, knobColor, 0.2f);
        }

        private void ReleaseKnob()
        {
            if (_knob != null)
            {
                UITween.Scale(_knob, 1f, 0.35f).SetEase(UIEase.OutSpring);
            }
        }
    }
}

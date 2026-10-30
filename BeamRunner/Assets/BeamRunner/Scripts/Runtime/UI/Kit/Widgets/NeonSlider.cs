using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BeamRunner.UI
{
    /// <summary>
    /// Neon slider: dark track, gradient fill that follows the accent, glowing knob that swells while dragged.
    /// Wraps a <see cref="ScrollAwareSlider"/> (a uGUI Slider: drag, tap-to-jump, keyboard/gamepad left/right steps 10 %)
    /// that hands mostly-vertical drags to an enclosing scroll list. While focused by keyboard/gamepad a
    /// <see cref="UIFocusRing"/> frames it and the knob lights up. Build with <see cref="UIFactory.Slider"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NeonSlider : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        private const float KnobPressedScale = 1.18f;
        private const float KnobFocusedScale = 1.1f;
        private const float KnobRestGlow = 0.6f;

        private ScrollAwareSlider _slider;
        private RectTransform _knob;
        private Image _knobGlow;
        private UIFocusRing _focus;
        private Action<float> _onChanged;
        private int _lastStep = int.MinValue;
        private bool _pressed;

        /// <summary>The underlying uGUI slider (min/max/wholeNumbers/navigation).</summary>
        public Slider Slider
        {
            get { return _slider; }
        }

        public RectTransform Rect
        {
            get { return (RectTransform)transform; }
        }

        public float Value
        {
            get { return _slider.value; }
            set { _slider.value = value; }
        }

        /// <summary>Sets the value without invoking the change callback.</summary>
        public void SetValueWithoutNotify(float value)
        {
            _slider.SetValueWithoutNotify(value);
        }

        internal void Build(float value, float min, float max, bool wholeNumbers, Action<float> onChanged, float width)
        {
            const float height = 88f;
            const float trackH = 14f;
            const float knob = 60f;
            RectTransform root = Rect;
            root.sizeDelta = new Vector2(width, height);
            UIFactory.SetLayout(this, width, height);
            _onChanged = onChanged;

            // Hit area over the whole control.
            root.gameObject.AddComponent<UIRaycastTarget>();

            Image track = UIFactory.CreateImage(root, null, UITheme.Track, "Track");
            RectTransform trt = track.rectTransform;
            trt.anchorMin = new Vector2(0f, 0.5f);
            trt.anchorMax = new Vector2(1f, 0.5f);
            trt.sizeDelta = new Vector2(0f, trackH);
            UIShape.MakePill(track);

            RectTransform fillArea = UIFactory.CreateRect(root, "FillArea");
            fillArea.anchorMin = new Vector2(0f, 0.5f);
            fillArea.anchorMax = new Vector2(1f, 0.5f);
            fillArea.offsetMin = new Vector2(knob * 0.5f, -trackH * 0.5f);
            fillArea.offsetMax = new Vector2(-knob * 0.5f, trackH * 0.5f);

            // The slider drives this container's anchors; the glow sits behind the bar.
            RectTransform frt = UIFactory.CreateRect(fillArea, "Fill");
            frt.anchorMin = Vector2.zero;
            frt.anchorMax = new Vector2(0f, 1f);
            frt.offsetMin = new Vector2(-knob * 0.5f, 0f);
            frt.offsetMax = Vector2.zero;
            Image fillGlow = UIFactory.CreateImage(frt, null, Color.white, "FillGlow");
            UISprites.ApplySoft(fillGlow, trackH * 0.5f, 18f);
            UIGradient.ApplyAccent(fillGlow, GradientDirection.Horizontal, 0.45f);
            Image fill = UIFactory.PillImage(frt, Color.white, "Bar");
            UIGradient.ApplyAccent(fill, GradientDirection.Horizontal);

            RectTransform handleArea = UIFactory.CreateRect(root, "HandleArea");
            handleArea.anchorMin = new Vector2(0f, 0.5f);
            handleArea.anchorMax = new Vector2(1f, 0.5f);
            handleArea.offsetMin = new Vector2(knob * 0.5f, -knob * 0.5f);
            handleArea.offsetMax = new Vector2(-knob * 0.5f, knob * 0.5f);

            RectTransform handle = UIFactory.CreateRect(handleArea, "Handle");
            handle.sizeDelta = new Vector2(knob, 0f);

            _knob = UIFactory.CreateFill(handle, "Knob");
            _knobGlow = UIFactory.CreateImage(_knob, UISprites.Glow, UITheme.Accent, "KnobGlow");
            UIFactory.Stretch(_knobGlow.rectTransform, -knob * 0.55f, -knob * 0.55f, -knob * 0.55f, -knob * 0.55f);
            UIAccentFollower.Attach(_knobGlow, AccentSlot.Primary, 0.6f);
            _knobGlow.canvasRenderer.SetAlpha(KnobRestGlow);
            Image body = UIFactory.CreateImage(_knob, UISprites.Circle, UITheme.TextPrimary, "Body");
            UIFactory.Stretch(body.rectTransform);
            Image ring = UIFactory.CreateImage(_knob, UISprites.Ring(28), UITheme.Accent, "Ring");
            UIFactory.Stretch(ring.rectTransform);
            UIAccentFollower.Attach(ring, AccentSlot.Primary);

            _slider = root.gameObject.AddComponent<ScrollAwareSlider>();
            _slider.transition = Selectable.Transition.None;
            _slider.direction = UnityEngine.UI.Slider.Direction.LeftToRight;
            _slider.fillRect = frt;
            _slider.handleRect = handle;
            _slider.targetGraphic = body;
            _slider.minValue = min;
            _slider.maxValue = max;
            _slider.wholeNumbers = wholeNumbers;
            _slider.SetValueWithoutNotify(value);
            _slider.onValueChanged.AddListener(HandleChanged);
            _slider.ScrollStarted += HandleScrollStarted;
            _lastStep = Mathf.RoundToInt(_slider.normalizedValue * 20f);

            _focus = UIFocusRing.Attach(_slider, root, height * 0.5f, 4f);
            if (_focus != null)
            {
                _focus.FocusChanged += HandleFocusChanged;
            }
        }

        private void HandleChanged(float v)
        {
            int step = _slider.wholeNumbers ? Mathf.RoundToInt(v) : Mathf.RoundToInt(_slider.normalizedValue * 20f);
            if (step != _lastStep)
            {
                _lastStep = step;
                UIFactory.PlayClickFeedback(false, true);
            }

            if (_onChanged != null)
            {
                try
                {
                    _onChanged(v);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (_knob == null || !_slider.IsInteractable() || eventData.button != PointerEventData.InputButton.Left)
            {
                return;
            }

            _pressed = true;
            UITween.Scale(_knob, KnobPressedScale, 0.16f).SetEase(UIEase.OutBack);
            UITween.RendererAlpha(_knobGlow, 1f, 0.16f);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (_knob == null || !_pressed)
            {
                return;
            }

            _pressed = false;
            ReleaseKnob();
        }

        private void OnDisable()
        {
            _pressed = false;
            if (_knob != null)
            {
                UITween.Kill(_knob);
                _knob.localScale = Vector3.one;
                UITween.Kill(_knobGlow);
                _knobGlow.canvasRenderer.SetAlpha(KnobRestGlow);
            }
        }

        // The press turned into a list scroll: the knob is not being held after all.
        private void HandleScrollStarted()
        {
            if (_pressed)
            {
                _pressed = false;
                ReleaseKnob();
            }
        }

        private void HandleFocusChanged(bool focused)
        {
            if (!_pressed && _knob != null)
            {
                ReleaseKnob();
            }
        }

        // Rest (or focused) knob: focus keeps it lit — and slightly swollen, unless reduced motion — as a hint that
        // left/right now move it.
        private void ReleaseKnob()
        {
            bool focused = _focus != null && _focus.IsFocused;
            float scale = focused && !UITheme.ReducedMotion ? KnobFocusedScale : 1f;
            UITween.Scale(_knob, scale, 0.4f).SetEase(UIEase.OutSpring);
            UITween.RendererAlpha(_knobGlow, focused ? 1f : KnobRestGlow, 0.3f);
        }
    }
}

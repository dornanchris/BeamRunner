using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BeamRunner.UI
{
    /// <summary>
    /// Keyboard/gamepad focus highlight for kit controls without <see cref="NeonButton"/> visuals (toggles, sliders,
    /// segments): the Secondary button's neon frame (accent, stroke 4, glow 22) fades in around the control while it is
    /// selected by navigation — not when a pointer selected it, exactly like <see cref="NeonButton"/> — and an optional
    /// target swells to NeonButton's selected scale (1.035). <see cref="UITheme.ReducedMotion"/> turns the swell off.
    /// Lives on the <see cref="Selectable"/>'s GameObject (it receives the select/deselect events); add it with
    /// <see cref="Attach"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UIFocusRing : MonoBehaviour, ISelectHandler, IDeselectHandler
    {
        /// <summary>NeonButton's scale while selected.</summary>
        public const float SwellScale = 1.035f;

        private const float Stroke = 4f;
        private const float Glow = 22f;
        private const float FadeSeconds = 0.25f;
        private const float SwellSeconds = 0.22f;

        private Image _frame;
        private RectTransform _swell;
        private float _swellScale = SwellScale;
        private bool _navSelected;
        private bool _shown;

        /// <summary>Raised when the highlight turns on (true) or off (false).</summary>
        public event Action<bool> FocusChanged;

        /// <summary>True while the control shows its focus highlight.</summary>
        public bool IsFocused
        {
            get { return _shown; }
        }

        /// <summary>
        /// Adds the highlight to <paramref name="selectable"/>'s GameObject. The frame is a pill-/rounded-rect
        /// <paramref name="outset"/> units outside <paramref name="host"/> (negative = inside) with corner radius
        /// <paramref name="radius"/> + <paramref name="outset"/>; <paramref name="swell"/> (optional) scales while focused.
        /// </summary>
        public static UIFocusRing Attach(Selectable selectable, RectTransform host, float radius, float outset = 6f,
            RectTransform swell = null, float swellScale = SwellScale)
        {
            if (selectable == null || host == null)
            {
                return null;
            }

            UIFocusRing ring = selectable.GetComponent<UIFocusRing>();
            if (ring == null)
            {
                ring = selectable.gameObject.AddComponent<UIFocusRing>();
            }

            if (ring._frame == null)
            {
                RectTransform area = UIFactory.CreateRect(host, "FocusRing");
                UIFactory.IgnoreLayout(area);
                UIFactory.Stretch(area, -outset, -outset, -outset, -outset);
                ring._frame = UIFactory.CreateImage(area, null, UITheme.Accent, "Frame");
                UISprites.ApplyFrame(ring._frame, Mathf.Max(2f, radius + outset), Stroke, Glow);
                UIAccentFollower.Attach(ring._frame, AccentSlot.Primary);
                ring._frame.canvasRenderer.SetAlpha(0f);
            }

            ring._swell = swell;
            ring._swellScale = swellScale;
            return ring;
        }

        public void OnSelect(BaseEventData eventData)
        {
            _navSelected = !(eventData is PointerEventData);
            Apply(true, true);
        }

        public void OnDeselect(BaseEventData eventData)
        {
            _navSelected = false;
            Apply(true, true);
        }

        private void OnEnable()
        {
            // Re-activated while still selected (e.g. a screen shown again): keep the highlight; otherwise clear it.
            EventSystem es = EventSystem.current;
            if (es == null || es.currentSelectedGameObject != gameObject)
            {
                _navSelected = false;
            }

            Apply(false, true);
        }

        private void OnDisable()
        {
            // No deselect event arrives for a deactivated selection: reset the visuals so it never comes back stale.
            Apply(false, false);
        }

        private void Apply(bool animate, bool allowOn)
        {
            bool on = allowOn && _navSelected;
            bool changed = on != _shown;
            _shown = on;
            float alpha = on ? 1f : 0f;
            float scale = on && !UITheme.ReducedMotion ? _swellScale : 1f;
            if (!animate || !Application.isPlaying)
            {
                if (_frame != null)
                {
                    UITween.Kill(_frame);
                    _frame.canvasRenderer.SetAlpha(alpha);
                }

                if (_swell != null)
                {
                    UITween.Kill(_swell);
                    _swell.localScale = new Vector3(scale, scale, 1f);
                }
            }
            else
            {
                if (_frame != null)
                {
                    UITween.RendererAlpha(_frame, alpha, FadeSeconds).SetEase(UIEase.OutQuad);
                }

                if (_swell != null)
                {
                    UITween.Scale(_swell, new Vector3(scale, scale, 1f), SwellSeconds).SetEase(UIEase.OutCubic);
                }
            }

            if (changed)
            {
                Action<bool> handler = FocusChanged;
                if (handler != null)
                {
                    try
                    {
                        handler(on);
                    }
                    catch (Exception e)
                    {
                        Debug.LogException(e, this);
                    }
                }
            }
        }
    }
}

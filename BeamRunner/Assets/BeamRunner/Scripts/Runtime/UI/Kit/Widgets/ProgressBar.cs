using UnityEngine;
using UnityEngine.UI;

namespace BeamRunner.UI
{
    /// <summary>
    /// Horizontal pill progress bar with an animated gradient fill and a glowing leading edge.
    /// Build with <see cref="UIFactory.ProgressBar"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ProgressBar : MonoBehaviour
    {
        private RectTransform _fill;
        private Image _bar;
        private Image _edge;
        private UIGradient _gradient;
        private float _value;
        private float _shown;
        private int _tween;

        public RectTransform Rect
        {
            get { return (RectTransform)transform; }
        }

        /// <summary>Target value 0..1.</summary>
        public float Value
        {
            get { return _value; }
        }

        /// <summary>The fill bar image (gradient).</summary>
        public Image Bar
        {
            get { return _bar; }
        }

        /// <summary>Sets the value (0..1), animating the fill with an ease-out by default.</summary>
        public void SetValue(float value, bool animate = true, float duration = 0.45f)
        {
            _value = Mathf.Clamp01(value);
            UITween.KillId(_tween);
            _tween = 0;
            if (!animate || !Application.isPlaying || Mathf.Abs(_value - _shown) < 0.0005f)
            {
                Place(_value);
                return;
            }

            _tween = UITween.Value(this, _shown, _value, duration, Place).SetEase(UIEase.OutCubic).Id;
        }

        /// <summary>Fixed gradient colours (stops following the accent).</summary>
        public void SetColors(Color from, Color to)
        {
            _gradient.FollowAccent = false;
            _gradient.Set(from, to);
            UIAccentFollower.Detach(_edge);
            _edge.color = UITheme.Alpha(to, 0.85f);
        }

        /// <summary>Shows/hides the glowing leading edge.</summary>
        public void SetEdgeGlow(bool visible)
        {
            if (_edge != null)
            {
                _edge.enabled = visible;
            }
        }

        internal void Build(float value, float height, Color? from, Color? to)
        {
            RectTransform root = Rect;
            root.sizeDelta = new Vector2(600f, height);
            UIFactory.SetLayout(this, -1f, height);

            UIFactory.PillImage(root, UITheme.Track, "Track");

            _fill = UIFactory.CreateRect(root, "Fill");
            _fill.anchorMin = Vector2.zero;
            _fill.anchorMax = new Vector2(0f, 1f);
            _fill.pivot = new Vector2(0f, 0.5f);
            _fill.offsetMin = Vector2.zero;
            _fill.offsetMax = Vector2.zero;

            Image glow = UIFactory.CreateImage(_fill, null, Color.white, "Glow");
            UISprites.ApplySoft(glow, height * 0.5f, Mathf.Clamp(height, 12f, 28f));
            _bar = UIFactory.PillImage(_fill, Color.white, "Bar");
            Image sheen = UIFactory.PillImage(_fill, Color.white, "Sheen");
            UIFactory.Stretch(sheen.rectTransform, 2f, 2f, 2f, height * 0.5f);
            UIGradient.Apply(sheen, new Color(1f, 1f, 1f, 0.35f), new Color(1f, 1f, 1f, 0f), GradientDirection.Vertical);

            _edge = UIFactory.CreateImage(_fill, UISprites.Glow, UITheme.Accent, "Edge");
            RectTransform ert = _edge.rectTransform;
            ert.anchorMin = new Vector2(1f, 0.5f);
            ert.anchorMax = new Vector2(1f, 0.5f);
            ert.sizeDelta = new Vector2(height * 3.2f, height * 3.2f);
            ert.anchoredPosition = new Vector2(-height * 0.35f, 0f);

            if (from.HasValue || to.HasValue)
            {
                Color a = from ?? UITheme.AccentAlt;
                Color b = to ?? UITheme.Accent;
                _gradient = UIGradient.Apply(_bar, a, b, GradientDirection.Horizontal);
                UIGradient.Apply(glow, UITheme.Alpha(a, 0.5f), UITheme.Alpha(b, 0.5f), GradientDirection.Horizontal);
                _edge.color = UITheme.Alpha(b, 0.85f);
            }
            else
            {
                _gradient = UIGradient.ApplyAccent(_bar, GradientDirection.Horizontal);
                UIGradient.ApplyAccent(glow, GradientDirection.Horizontal, 0.5f);
                UIAccentFollower.Attach(_edge, AccentSlot.Primary, 0.85f, 0.25f);
            }

            _value = Mathf.Clamp01(value);
            Place(_value);
        }

        private void Place(float v)
        {
            _shown = v;
            if (_fill == null)
            {
                return;
            }

            _fill.anchorMax = new Vector2(v, 1f);
            _fill.offsetMax = Vector2.zero;
            bool visible = v > 0.001f;
            if (_fill.gameObject.activeSelf != visible)
            {
                _fill.gameObject.SetActive(visible);
            }
        }
    }
}

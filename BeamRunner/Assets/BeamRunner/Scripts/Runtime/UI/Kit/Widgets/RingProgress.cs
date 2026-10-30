using UnityEngine;
using UnityEngine.UI;

namespace BeamRunner.UI
{
    /// <summary>
    /// Radial progress ring (Image.Type.Filled, Radial360 from the top, clockwise) with a glowing head dot and a
    /// centre label. Build with <see cref="UIFactory.RingProgress"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RingProgress : MonoBehaviour
    {
        private Image _fill;
        private Image _glow;
        private RectTransform _headPivot;
        private Image _head;
        private Text _label;
        private float _value;
        private float _shown;
        private int _tween;

        public RectTransform Rect
        {
            get { return (RectTransform)transform; }
        }

        public float Value
        {
            get { return _value; }
        }

        /// <summary>Centre label (empty by default).</summary>
        public Text Label
        {
            get { return _label; }
        }

        public Image Fill
        {
            get { return _fill; }
        }

        public void SetValue(float value, bool animate = true, float duration = 0.6f)
        {
            _value = Mathf.Clamp01(value);
            UITween.KillId(_tween);
            _tween = 0;
            if (!animate || !Application.isPlaying)
            {
                Place(_value);
                return;
            }

            _tween = UITween.Value(this, _shown, _value, duration, Place).SetEase(UIEase.OutCubic).Id;
        }

        /// <summary>Fixed colour (stops following the accent).</summary>
        public void SetColor(Color color)
        {
            UIAccentFollower.Detach(_fill);
            UIAccentFollower.Detach(_head);
            UIAccentFollower.Detach(_glow);
            _fill.color = color;
            _head.color = UITheme.Lighten(color, 0.4f);
            _glow.color = UITheme.Alpha(color, 0.35f);
        }

        internal void Build(float size, float thickness, float value, Color? color)
        {
            RectTransform root = Rect;
            root.sizeDelta = new Vector2(size, size);
            UIFactory.SetLayout(this, size, size);
            int px = Mathf.Clamp(Mathf.RoundToInt(thickness * 256f / Mathf.Max(1f, size)), 2, 120);
            Sprite ring = UISprites.Ring(px);

            _glow = UIFactory.CreateImage(root, UISprites.Glow, UITheme.Alpha(color ?? UITheme.Accent, 0.3f), "Glow");
            UIFactory.Stretch(_glow.rectTransform, -size * 0.2f, -size * 0.2f, -size * 0.2f, -size * 0.2f);

            Image track = UIFactory.CreateImage(root, ring, UITheme.Track, "Track");
            UIFactory.Stretch(track.rectTransform);

            _fill = UIFactory.CreateImage(root, ring, color ?? UITheme.Accent, "Fill");
            UIFactory.Stretch(_fill.rectTransform);
            _fill.type = Image.Type.Filled;
            _fill.fillMethod = Image.FillMethod.Radial360;
            _fill.fillOrigin = (int)Image.Origin360.Top;
            _fill.fillClockwise = true;
            _fill.fillAmount = 0f;

            _headPivot = UIFactory.CreateFill(root, "HeadPivot");
            float r = size * 0.5f - (px * size / 256f) * 0.5f - size / 256f;
            _head = UIFactory.CreateImage(_headPivot, UISprites.Glow, Color.white, "Head");
            RectTransform hrt = _head.rectTransform;
            hrt.anchorMin = new Vector2(0.5f, 0.5f);
            hrt.anchorMax = new Vector2(0.5f, 0.5f);
            float hs = Mathf.Max(thickness * 2.6f, 18f);
            hrt.sizeDelta = new Vector2(hs, hs);
            hrt.anchoredPosition = new Vector2(0f, r);

            if (!color.HasValue)
            {
                UIAccentFollower.Attach(_fill, AccentSlot.Primary);
                UIAccentFollower.Attach(_glow, AccentSlot.Primary, 0.3f);
                UIAccentFollower.Attach(_head, AccentSlot.Primary, 1f, 0.55f);
            }
            else
            {
                _head.color = UITheme.Lighten(color.Value, 0.55f);
            }

            _label = UIFactory.LabelLine(root, string.Empty, Mathf.RoundToInt(size * 0.24f), UITheme.TextPrimary, TextAnchor.MiddleCenter, UIFont.Title);
            UIFactory.Stretch(_label.rectTransform);

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

            _fill.fillAmount = v;
            _headPivot.localEulerAngles = new Vector3(0f, 0f, -360f * v);
            bool showHead = v > 0.001f && v < 0.999f;
            if (_head.enabled != showHead)
            {
                _head.enabled = showHead;
            }
        }
    }
}

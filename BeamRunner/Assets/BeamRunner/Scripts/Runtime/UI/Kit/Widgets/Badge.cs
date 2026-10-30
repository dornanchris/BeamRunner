using UnityEngine;
using UnityEngine.UI;

namespace BeamRunner.UI
{
    /// <summary>
    /// Notification badge pinned to a corner of its parent: a red dot (count &lt; 0) or a count bubble (1..99+),
    /// hidden at 0. Pops in with a spring and punches when the count grows. Build with <see cref="UIFactory.Badge"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Badge : MonoBehaviour
    {
        private const float DotSize = 26f;
        private const float BubbleHeight = 46f;

        private Image _bg;
        private Image _glow;
        private Text _text;
        private int _count = int.MinValue;
        private bool _visible;

        public int Count
        {
            get { return _count; }
        }

        public RectTransform Rect
        {
            get { return (RectTransform)transform; }
        }

        /// <summary>0 hides the badge, a negative value shows a plain dot, positive values show the number.</summary>
        public void SetCount(int count, bool animate = true)
        {
            if (count == _count)
            {
                return;
            }

            int previous = _count;
            _count = count;
            bool show = count != 0;
            RectTransform rt = Rect;
            if (count < 0)
            {
                _text.text = string.Empty;
                rt.sizeDelta = new Vector2(DotSize, DotSize);
            }
            else if (count > 0)
            {
                _text.text = count > 99 ? "99+" : count.ToString(System.Globalization.CultureInfo.InvariantCulture);
                float w = Mathf.Max(BubbleHeight, _text.preferredWidth + 26f);
                rt.sizeDelta = new Vector2(w, BubbleHeight);
            }

            animate &= Application.isPlaying;
            if (show && !_visible)
            {
                _visible = true;
                gameObject.SetActive(true);
                if (animate)
                {
                    UITween.Scale(rt, 0.01f, 1f, 0.42f).SetEase(UIEase.OutBack);
                }
                else
                {
                    rt.localScale = Vector3.one;
                }
            }
            else if (!show && _visible)
            {
                _visible = false;
                if (animate)
                {
                    UITween.Scale(rt, 0.01f, 0.18f).SetEase(UIEase.InBack).OnComplete(HideNow);
                }
                else
                {
                    HideNow();
                }
            }
            else if (show && animate && count > previous)
            {
                UITween.Punch(rt, 0.3f, 0.4f);
            }
        }

        private void HideNow()
        {
            if (!_visible)
            {
                gameObject.SetActive(false);
            }
        }

        internal void Build(int count, Color? color)
        {
            RectTransform rt = Rect;
            rt.anchorMin = Vector2.one;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(-10f, -10f);
            UIFactory.IgnoreLayout(this);
            Color c = color ?? UITheme.Danger;
            _glow = UIFactory.CreateImage(rt, UISprites.Glow, UITheme.Alpha(c, 0.55f), "Glow");
            UIFactory.Stretch(_glow.rectTransform, -18f, -18f, -18f, -18f);
            _bg = UIFactory.PillImage(rt, c, "Bubble");
            Image rim = UIFactory.CreateImage(rt, null, new Color(1f, 1f, 1f, 0.9f), "Rim");
            UIFactory.Stretch(rim.rectTransform);
            UISprites.ApplyOutline(rim, BubbleHeight * 0.5f, 3f);
            _text = UIFactory.LabelLine(rt, string.Empty, 30, UITheme.TextPrimary, TextAnchor.MiddleCenter, UIFont.Bold);
            UIFactory.Stretch(_text.rectTransform, 0f, 0f, 0f, 2f);
            _visible = true;
            _count = int.MinValue;
            SetCount(count, false);
            if (count == 0)
            {
                _visible = false;
                gameObject.SetActive(false);
            }
        }
    }
}

using UnityEngine;
using UnityEngine.UI;

namespace BeamRunner.UI
{
    /// <summary>
    /// Dark translucent glass panel: soft drop shadow, vertical-tinted fill, faint scanlines, top sheen and a neon
    /// frame with outer glow. Put children under <see cref="Content"/> (inset by the padding). Build with
    /// <see cref="UIFactory.GlassPanel"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GlassPanel : MonoBehaviour
    {
        private Image _shadow;
        private Image _fill;
        private Image _scan;
        private Image _sheen;
        private Image _frame;
        private RectTransform _content;
        private float _radius;

        public RectTransform Rect
        {
            get { return (RectTransform)transform; }
        }

        /// <summary>Container for the panel's children (stretched, inset by the padding).</summary>
        public RectTransform Content
        {
            get { return _content; }
        }

        /// <summary>The glass fill (raycast target: blocks clicks behind the panel).</summary>
        public Image Background
        {
            get { return _fill; }
        }

        public Image Frame
        {
            get { return _frame; }
        }

        public float Radius
        {
            get { return _radius; }
        }

        /// <summary>Fixed frame colour (stops following the theme accent); null = follow the accent again.</summary>
        public void SetAccent(Color? color)
        {
            if (_frame == null)
            {
                return;
            }

            if (color.HasValue)
            {
                UIAccentFollower.Detach(_frame);
                _frame.color = color.Value;
            }
            else
            {
                UIAccentFollower.Attach(_frame, AccentSlot.Primary, 0.95f);
            }
        }

        /// <summary>Shows or hides the neon frame (e.g. to highlight a selected card).</summary>
        public void SetFrameVisible(bool visible, bool animate = true)
        {
            if (_frame == null)
            {
                return;
            }

            if (!animate)
            {
                UITween.Kill(_frame);
                _frame.canvasRenderer.SetAlpha(visible ? 1f : 0f);
                return;
            }

            UITween.RendererAlpha(_frame, visible ? 1f : 0f, 0.25f).SetEase(UIEase.OutCubic);
        }

        internal void Build(Color? accent, float radius, float padding, bool scanlines, bool shadow, bool frame, bool content = true)
        {
            _radius = radius;
            RectTransform root = Rect;
            if (shadow)
            {
                _shadow = UIFactory.CreateImage(root, null, UITheme.Shadow, "Shadow");
                UISprites.ApplySoft(_shadow, radius, 42f, new Vector2(0f, -14f));
                UIFactory.IgnoreLayout(_shadow);
            }

            _fill = UIFactory.CreateImage(root, null, UITheme.PanelGlass, "Glass", true);
            UIFactory.Stretch(_fill.rectTransform);
            UIShape.Rounded(_fill, radius);
            UIGradient.Apply(_fill, new Color(1.25f, 1.2f, 1.3f, 1f), new Color(0.85f, 0.85f, 0.95f, 1f), GradientDirection.Vertical);
            UIFactory.IgnoreLayout(_fill);

            if (scanlines)
            {
                _scan = UIFactory.CreateImage(root, null, new Color(1f, 1f, 1f, 0.035f), "Scanlines");
                UISprites.ApplyTiled(_scan, UISprites.Scanlines);
                float inset = Mathf.Ceil(radius * 0.32f) + 2f;
                UIFactory.Stretch(_scan.rectTransform, inset, inset, inset, inset);
                UIFactory.IgnoreLayout(_scan);
            }

            _sheen = UIFactory.CreateImage(root, null, Color.white, "Sheen");
            RectTransform srt = _sheen.rectTransform;
            srt.anchorMin = new Vector2(0f, 0.55f);
            srt.anchorMax = Vector2.one;
            srt.offsetMin = new Vector2(3f, 0f);
            srt.offsetMax = new Vector2(-3f, -3f);
            UIShape.Rounded(_sheen, Mathf.Max(2f, radius - 3f));
            UIGradient.Apply(_sheen, new Color(1f, 1f, 1f, 0.075f), new Color(1f, 1f, 1f, 0f), GradientDirection.Vertical);
            UIFactory.IgnoreLayout(_sheen);

            if (frame)
            {
                _frame = UIFactory.CreateImage(root, null, accent ?? UITheme.Accent, "Frame");
                UISprites.ApplyFrame(_frame, radius, UITheme.BorderWidth, UITheme.GlowSize);
                UIFactory.IgnoreLayout(_frame);
                if (!accent.HasValue)
                {
                    UIAccentFollower.Attach(_frame, AccentSlot.Primary, 0.95f);
                }
            }

            if (content)
            {
                _content = UIFactory.CreateFill(root, "Content");
                UIFactory.Pad(_content, padding);
            }
            else
            {
                _content = root;
            }
        }
    }
}

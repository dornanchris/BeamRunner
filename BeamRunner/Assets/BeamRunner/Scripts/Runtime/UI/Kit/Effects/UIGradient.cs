using UnityEngine;
using UnityEngine.UI;

namespace BeamRunner.UI
{
    /// <summary>Gradient direction for <see cref="UIGradient"/>.</summary>
    public enum GradientDirection
    {
        /// <summary><see cref="UIGradient.From"/> on the left, <see cref="UIGradient.To"/> on the right.</summary>
        Horizontal = 0,

        /// <summary><see cref="UIGradient.From"/> at the top, <see cref="UIGradient.To"/> at the bottom.</summary>
        Vertical = 1,

        /// <summary>From the top-left corner to the bottom-right corner.</summary>
        Diagonal = 2,
    }

    /// <summary>
    /// Two-stop gradient applied to a graphic's vertex colours (multiplied with <c>Graphic.color</c>). Works with sliced
    /// images and text; linear gradients are exact because slice vertices interpolate linearly. Can follow the theme accent.
    /// </summary>
    [RequireComponent(typeof(Graphic))]
    [DisallowMultipleComponent]
    public sealed class UIGradient : BaseMeshEffect
    {
        private Color _from = Color.white;
        private Color _to = Color.white;
        private GradientDirection _direction = GradientDirection.Horizontal;
        private bool _followAccent;
        private float _accentAlpha = 1f;
        private UIVertex _vertex;

        public Color From
        {
            get { return _from; }
            set
            {
                _from = value;
                Dirty();
            }
        }

        public Color To
        {
            get { return _to; }
            set
            {
                _to = value;
                Dirty();
            }
        }

        public GradientDirection Direction
        {
            get { return _direction; }
            set
            {
                _direction = value;
                Dirty();
            }
        }

        /// <summary>When true, <see cref="From"/> = <see cref="UITheme.AccentAlt"/> and <see cref="To"/> = <see cref="UITheme.Accent"/>, updated live.</summary>
        public bool FollowAccent
        {
            get { return _followAccent; }
            set
            {
                if (_followAccent == value)
                {
                    return;
                }

                _followAccent = value;
                if (isActiveAndEnabled)
                {
                    if (value)
                    {
                        UITheme.AccentChanged += OnAccentChanged;
                        OnAccentChanged();
                    }
                    else
                    {
                        UITheme.AccentChanged -= OnAccentChanged;
                    }
                }
            }
        }

        /// <summary>Adds or configures a gradient on <paramref name="graphic"/>.</summary>
        public static UIGradient Apply(Graphic graphic, Color from, Color to, GradientDirection direction)
        {
            UIGradient g = graphic.GetComponent<UIGradient>();
            if (g == null)
            {
                g = graphic.gameObject.AddComponent<UIGradient>();
            }

            g.FollowAccent = false;
            g._from = from;
            g._to = to;
            g._direction = direction;
            g.Dirty();
            return g;
        }

        /// <summary>Accent-following gradient (AccentAlt → Accent) with an overall alpha.</summary>
        public static UIGradient ApplyAccent(Graphic graphic, GradientDirection direction, float alpha = 1f)
        {
            UIGradient g = Apply(graphic, Color.white, Color.white, direction);
            g._accentAlpha = alpha;
            g.FollowAccent = true;
            g.OnAccentChanged();
            return g;
        }

        public void Set(Color from, Color to)
        {
            _from = from;
            _to = to;
            Dirty();
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            if (_followAccent)
            {
                UITheme.AccentChanged += OnAccentChanged;
                OnAccentChanged();
            }
        }

        protected override void OnDisable()
        {
            if (_followAccent)
            {
                UITheme.AccentChanged -= OnAccentChanged;
            }

            base.OnDisable();
        }

        private void OnAccentChanged()
        {
            _from = UITheme.Alpha(UITheme.AccentAlt, _accentAlpha);
            _to = UITheme.Alpha(UITheme.Accent, _accentAlpha);
            Dirty();
        }

        private void Dirty()
        {
            Graphic g = graphic;
            if (g != null)
            {
                g.SetVerticesDirty();
            }
        }

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive() || vh.currentVertCount == 0)
            {
                return;
            }

            Rect r = graphic.rectTransform.rect;
            float w = r.width > 0.0001f ? r.width : 1f;
            float h = r.height > 0.0001f ? r.height : 1f;
            int count = vh.currentVertCount;
            for (int i = 0; i < count; i++)
            {
                vh.PopulateUIVertex(ref _vertex, i);
                float u = (_vertex.position.x - r.xMin) / w;
                float v = (r.yMax - _vertex.position.y) / h;
                float t;
                switch (_direction)
                {
                    case GradientDirection.Vertical:
                        t = v;
                        break;
                    case GradientDirection.Diagonal:
                        t = (u + v) * 0.5f;
                        break;
                    default:
                        t = u;
                        break;
                }

                Color c = Color.LerpUnclamped(_from, _to, Mathf.Clamp01(t));
                Color32 baseColor = _vertex.color;
                _vertex.color = new Color32(
                    ToByte(baseColor.r * c.r),
                    ToByte(baseColor.g * c.g),
                    ToByte(baseColor.b * c.b),
                    ToByte(baseColor.a * c.a));
                vh.SetUIVertex(_vertex, i);
            }
        }

        private static byte ToByte(float v)
        {
            return (byte)(v <= 0f ? 0 : (v >= 255f ? 255 : (int)(v + 0.5f)));
        }
    }
}

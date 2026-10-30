using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BeamRunner.UI
{
    /// <summary>
    /// Keeps an <see cref="Image"/> a rounded rectangle of a given corner radius — or a perfect pill (radius = half the
    /// shorter side) that stays correct when the rect is resized by layout or animation.
    /// </summary>
    [RequireComponent(typeof(Image))]
    [DisallowMultipleComponent]
    public sealed class UIShape : UIBehaviour
    {
        private Image _image;
        private float _radius = UITheme.RadiusMd;
        private bool _pill;
        private float _appliedRadius = -1f;

        /// <summary>Corner radius in canvas units (ignored when <see cref="Pill"/>).</summary>
        public float Radius
        {
            get { return _radius; }
            set
            {
                _radius = value;
                Refresh();
            }
        }

        public bool Pill
        {
            get { return _pill; }
            set
            {
                _pill = value;
                Refresh();
            }
        }

        /// <summary>Adds (or reuses) a UIShape on <paramref name="image"/> with a fixed radius.</summary>
        public static UIShape Rounded(Image image, float radius)
        {
            UIShape s = image.GetComponent<UIShape>();
            if (s == null)
            {
                s = image.gameObject.AddComponent<UIShape>();
            }

            s._pill = false;
            s._radius = radius;
            s._appliedRadius = -1f;
            s.Refresh();
            return s;
        }

        /// <summary>Adds (or reuses) a UIShape on <paramref name="image"/> in pill mode.</summary>
        public static UIShape MakePill(Image image)
        {
            UIShape s = image.GetComponent<UIShape>();
            if (s == null)
            {
                s = image.gameObject.AddComponent<UIShape>();
            }

            s._pill = true;
            s._appliedRadius = -1f;
            s.Refresh();
            return s;
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            Refresh();
        }

        protected override void OnRectTransformDimensionsChange()
        {
            base.OnRectTransformDimensionsChange();
            if (_pill)
            {
                Refresh();
            }
        }

        public void Refresh()
        {
            if (_image == null)
            {
                _image = GetComponent<Image>();
                if (_image == null)
                {
                    return;
                }
            }

            float r = _radius;
            if (_pill)
            {
                Rect rect = ((RectTransform)transform).rect;
                r = Mathf.Min(rect.width, rect.height) * 0.5f;
                if (r < 1f)
                {
                    // Not laid out yet: use a typical pill radius so the first frame still looks right.
                    r = _appliedRadius > 0f ? _appliedRadius : 32f;
                }
            }

            if (Mathf.Abs(r - _appliedRadius) < 0.05f && _image.sprite != null)
            {
                return;
            }

            _appliedRadius = r;
            UISprites.ApplyRounded(_image, r);
        }
    }
}

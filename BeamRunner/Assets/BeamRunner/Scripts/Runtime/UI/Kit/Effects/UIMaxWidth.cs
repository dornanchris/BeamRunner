using UnityEngine;
using UnityEngine.EventSystems;

namespace BeamRunner.UI
{
    /// <summary>
    /// Keeps a horizontally stretched rect centred in its parent with a width of at most <see cref="MaxWidth"/> and
    /// side insets of at least <see cref="MinInset"/>. Vertical anchors/offsets are left untouched.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UIMaxWidth : UIBehaviour
    {
        private float _maxWidth = UITheme.ContentMaxWidth;
        private float _minInset;
        private bool _applying;

        public float MaxWidth
        {
            get { return _maxWidth; }
            set
            {
                _maxWidth = value;
                Apply();
            }
        }

        public float MinInset
        {
            get { return _minInset; }
            set
            {
                _minInset = value;
                Apply();
            }
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            Apply();
        }

        protected override void OnRectTransformDimensionsChange()
        {
            base.OnRectTransformDimensionsChange();
            Apply();
        }

        protected override void OnTransformParentChanged()
        {
            base.OnTransformParentChanged();
            Apply();
        }

        public void Apply()
        {
            if (_applying)
            {
                return;
            }

            var rt = transform as RectTransform;
            var parent = rt != null ? rt.parent as RectTransform : null;
            if (parent == null)
            {
                return;
            }

            float pw = parent.rect.width;
            float inset = Mathf.Max(_minInset, (pw - _maxWidth) * 0.5f);
            _applying = true;
            rt.anchorMin = new Vector2(0f, rt.anchorMin.y);
            rt.anchorMax = new Vector2(1f, rt.anchorMax.y);
            Vector2 omin = rt.offsetMin;
            Vector2 omax = rt.offsetMax;
            if (!Mathf.Approximately(omin.x, inset) || !Mathf.Approximately(omax.x, -inset))
            {
                rt.offsetMin = new Vector2(inset, omin.y);
                rt.offsetMax = new Vector2(-inset, omax.y);
            }

            _applying = false;
        }
    }
}

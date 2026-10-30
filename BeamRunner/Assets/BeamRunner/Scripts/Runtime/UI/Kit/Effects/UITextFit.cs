using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BeamRunner.UI
{
    /// <summary>
    /// Best-fit for single-line labels: when the text at its design size is wider than the label's rect, the font shrinks
    /// (never below <see cref="MinScale"/> of the design size) so it fits — longer translations (German, Russian, Polish)
    /// stay inside buttons and chips, while text that fits keeps its exact design size (English looks unchanged).
    /// <para>It reports the text's preferred width/height <b>at the design size</b> to the layout system (priority 0, the
    /// same as <see cref="Text"/>, whose smaller values then lose; an explicit <see cref="LayoutElement"/> still wins), so a
    /// layout group gives the label min(design width, available width) and shrinking never feeds back into the layout.
    /// Unity's own best fit is not used: it only shrinks vertically when horizontal overflow is on, and wraps otherwise.</para>
    /// Added by <see cref="UIFactory.LabelLine"/> (and so to every button label).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Text))]
    public sealed class UITextFit : UIBehaviour, ILayoutElement
    {
        /// <summary>Default lower bound: 62 % of the design size (still readable at the kit's caption sizes).</summary>
        public const float DefaultMinScale = 0.62f;

        private Text _text;
        private int _baseSize;
        private int _appliedSize = -1;
        private float _minScale = DefaultMinScale;
        private bool _busy;
        private bool _registered;

        private string _measuredText;
        private Font _measuredFont;
        private int _measuredSize = -1;
        private float _measuredWidth;
        private float _measuredHeight;

        /// <summary>Smallest scale of the design size the font may shrink to (0.3–1).</summary>
        public float MinScale
        {
            get { return _minScale; }
            set
            {
                _minScale = Mathf.Clamp(value, 0.3f, 1f);
                Apply();
            }
        }

        /// <summary>The design font size (the size used whenever the text fits).</summary>
        public int BaseSize
        {
            get { return _baseSize; }
        }

        /// <summary>True while the text is drawn smaller than its design size.</summary>
        public bool IsShrunk
        {
            get { return _text != null && _text.fontSize < _baseSize; }
        }

        /// <summary>Adds (or returns) the fit on <paramref name="text"/>; its current font size becomes the design size.</summary>
        public static UITextFit Attach(Text text, float minScale = DefaultMinScale)
        {
            if (text == null)
            {
                return null;
            }

            UITextFit fit = text.GetComponent<UITextFit>();
            if (fit == null)
            {
                fit = text.gameObject.AddComponent<UITextFit>();
            }

            fit.Init(text, minScale);
            return fit;
        }

        /// <summary>
        /// One-shot fit for a <b>wrapping</b> text in a fixed-size rect (toasts): sets the largest font size from
        /// <paramref name="designSize"/> down to <paramref name="minScale"/> of it at which the wrapped text fits the rect's
        /// height. CJK fonts have ~13 % taller lines than Rajdhani and CJK text wraps sooner, so a two-line English-sized
        /// box can hold fewer lines. Call after the text and the rect size are set; returns the size used.
        /// </summary>
        public static int FitWrapped(Text text, int designSize, float minScale = DefaultMinScale)
        {
            if (text == null)
            {
                return 0;
            }

            int size = Mathf.Max(1, designSize);
            Vector2 box = text.rectTransform.rect.size;
            if (box.x > 1f && box.y > 1f && text.font != null && !string.IsNullOrEmpty(text.text))
            {
                int min = Mathf.Max(1, Mathf.RoundToInt(size * Mathf.Clamp(minScale, 0.3f, 1f)));
                float ppu = text.pixelsPerUnit > 0f ? text.pixelsPerUnit : 1f;
                TextGenerator generator = text.cachedTextGeneratorForLayout;
                TextGenerationSettings settings = text.GetGenerationSettings(new Vector2(box.x, 0f));
                settings.resizeTextForBestFit = false;
                while (size > min)
                {
                    settings.fontSize = size;
                    if (generator.GetPreferredHeight(text.text, settings) / ppu <= box.y + 0.5f)
                    {
                        break;
                    }

                    size--;
                }
            }

            if (text.fontSize != size)
            {
                text.fontSize = size;
            }

            return size;
        }

        /// <summary>Changes the design size (use instead of setting <see cref="Text.fontSize"/> directly).</summary>
        public void SetBaseSize(int size)
        {
            _baseSize = Mathf.Max(1, size);
            _appliedSize = -1;
            Apply();
        }

        /// <summary>Width of the text at the design size, in canvas units.</summary>
        public float DesignWidth
        {
            get
            {
                Measure();
                return _measuredWidth;
            }
        }

        private void Init(Text text, float minScale)
        {
            _text = text;
            _minScale = Mathf.Clamp(minScale, 0.3f, 1f);
            if (_baseSize <= 0)
            {
                _baseSize = Mathf.Max(1, text.fontSize);
            }

            Register();
            Apply();
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            if (_text == null)
            {
                _text = GetComponent<Text>();
                if (_baseSize <= 0 && _text != null)
                {
                    _baseSize = Mathf.Max(1, _text.fontSize);
                }
            }

            Register();
            Apply();
        }

        protected override void OnDestroy()
        {
            if (_registered && _text != null)
            {
                _text.UnregisterDirtyLayoutCallback(OnTextDirty);
            }

            _registered = false;
            base.OnDestroy();
        }

        protected override void OnRectTransformDimensionsChange()
        {
            base.OnRectTransformDimensionsChange();
            Apply();
        }

        private void Register()
        {
            if (!_registered && _text != null)
            {
                _text.RegisterDirtyLayoutCallback(OnTextDirty);
                _registered = true;
            }
        }

        private void OnTextDirty()
        {
            // Text, font or size changed (our own size changes are ignored while _busy).
            if (!_busy)
            {
                Apply();
            }
        }

        /// <summary>Re-fits now (e.g. after the rect was resized by code).</summary>
        public void Apply()
        {
            if (_busy || _text == null || _baseSize <= 0)
            {
                return;
            }

            // Someone else set a size since our last change: that is the new design size.
            if (_appliedSize >= 0 && _text.fontSize != _appliedSize)
            {
                _baseSize = _text.fontSize;
            }

            int size = _baseSize;
            float available = ((RectTransform)transform).rect.width;
            if (available > 1f && !string.IsNullOrEmpty(_text.text))
            {
                Measure();
                if (_measuredWidth > available + 0.5f && _measuredWidth > 0f)
                {
                    int fitted = Mathf.FloorToInt(_baseSize * available / _measuredWidth);
                    size = Mathf.Clamp(fitted, Mathf.Max(1, Mathf.RoundToInt(_baseSize * _minScale)), _baseSize);
                }
            }

            _busy = true;
            try
            {
                if (_text.fontSize != size)
                {
                    _text.fontSize = size;
                }

                _appliedSize = size;
            }
            finally
            {
                _busy = false;
            }
        }

        private void Measure()
        {
            if (_text == null)
            {
                _measuredWidth = 0f;
                _measuredHeight = 0f;
                return;
            }

            string s = _text.text ?? string.Empty;
            if (ReferenceEquals(s, _measuredText) && _measuredFont == _text.font && _measuredSize == _baseSize)
            {
                return;
            }

            _measuredText = s;
            _measuredFont = _text.font;
            _measuredSize = _baseSize;
            if (s.Length == 0 || _text.font == null)
            {
                _measuredWidth = 0f;
                _measuredHeight = 0f;
                return;
            }

            TextGenerationSettings settings = _text.GetGenerationSettings(Vector2.zero);
            settings.fontSize = _baseSize;
            settings.resizeTextForBestFit = false;
            float ppu = _text.pixelsPerUnit > 0f ? _text.pixelsPerUnit : 1f;
            TextGenerator generator = _text.cachedTextGeneratorForLayout;
            _measuredWidth = generator.GetPreferredWidth(s, settings) / ppu;
            _measuredHeight = generator.GetPreferredHeight(s, settings) / ppu;
        }

        // ------------------------------------------------------------------ ILayoutElement (design-size preferred size)

        public void CalculateLayoutInputHorizontal()
        {
        }

        public void CalculateLayoutInputVertical()
        {
        }

        public float minWidth
        {
            get { return -1f; }
        }

        public float preferredWidth
        {
            get
            {
                Measure();
                return _measuredWidth;
            }
        }

        public float flexibleWidth
        {
            get { return -1f; }
        }

        public float minHeight
        {
            get { return -1f; }
        }

        public float preferredHeight
        {
            get
            {
                Measure();
                return _measuredHeight;
            }
        }

        public float flexibleHeight
        {
            get { return -1f; }
        }

        public int layoutPriority
        {
            get { return 0; }
        }
    }
}

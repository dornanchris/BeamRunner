using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BeamRunner.UI
{
    /// <summary>
    /// Display title with layered neon glow: a soft glow plate, two chromatic ghost copies (secondary accent left,
    /// primary accent right) and a bright core with a coloured halo. Breathes gently and flickers now and then
    /// (disabled by <see cref="UITheme.ReducedMotion"/>). Animation only touches CanvasRenderer alpha and ghost
    /// offsets, so it never rebuilds text meshes. Single line; the font shrinks (down to half size) when the text
    /// is wider than the rect. Build with <see cref="UIFactory.NeonTitle"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NeonTitle : UIBehaviour
    {
        private Text _main;
        private Text _ghostA;
        private Text _ghostB;
        private Image _plate;
        private Outline _halo;
        private bool _followAccent;
        private float _time;
        private float _nextFlicker;
        private float _flickerT = -1f;
        private float _ghostOffset;
        private bool _animate = true;
        private int _baseSize;
        private bool _fitting;

        public Text Text
        {
            get { return _main; }
        }

        public RectTransform Rect
        {
            get { return (RectTransform)transform; }
        }

        /// <summary>Enables breathing/flicker (on by default).</summary>
        public bool Animate
        {
            get { return _animate; }
            set
            {
                _animate = value;
                if (!value)
                {
                    ApplyAlpha(1f, 0.22f, 0.55f);
                }
            }
        }

        public void SetText(string text)
        {
            string s = text ?? string.Empty;
            _main.text = s;
            _ghostA.text = s;
            _ghostB.text = s;
            Fit();
        }

        /// <summary>
        /// Draws the title with <paramref name="font"/> instead of the current language's display face (the brand logo
        /// keeps Audiowide in every language: <c>UIFonts.For(LocScript.Latin, UIFont.Title)</c>).
        /// </summary>
        public void SetFont(Font font)
        {
            if (font == null || _main == null)
            {
                return;
            }

            _main.font = font;
            _ghostA.font = font;
            _ghostB.font = font;
            Fit();
        }

        /// <summary>Uses fixed colours instead of following the accent.</summary>
        public void SetColors(Color primary, Color secondary)
        {
            _followAccent = false;
            ApplyColors(primary, secondary);
        }

        internal void Build(string text, int size, TextAnchor align, Color? primary, Color? secondary)
        {
            RectTransform root = Rect;
            root.sizeDelta = new Vector2(1000f, Mathf.Ceil(size * 1.3f));
            // Own (nested) canvas: the per-frame glow animation then only re-batches this title.
            gameObject.AddComponent<Canvas>();

            UIFactory.SetLayout(this, -1f, Mathf.Ceil(size * 1.3f));

            _plate = UIFactory.CreateImage(root, UISprites.Glow, Color.white, "GlowPlate");
            RectTransform prt = _plate.rectTransform;
            prt.anchorMin = new Vector2(0.08f, -0.35f);
            prt.anchorMax = new Vector2(0.92f, 1.35f);
            prt.offsetMin = Vector2.zero;
            prt.offsetMax = Vector2.zero;

            _ghostOffset = Mathf.Max(2f, size * 0.035f);
            _ghostA = MakeText(root, "GhostA", text, size, align);
            _ghostB = MakeText(root, "GhostB", text, size, align);
            _main = MakeText(root, "Main", text, size, align);
            _halo = _main.gameObject.AddComponent<Outline>();
            float d = Mathf.Clamp(size * 0.03f, 1.5f, 4f);
            _halo.effectDistance = new Vector2(d, -d);
            _halo.useGraphicAlpha = true;
            Shadow drop = _main.gameObject.AddComponent<Shadow>();
            drop.effectColor = new Color(0f, 0f, 0.03f, 0.6f);
            drop.effectDistance = new Vector2(0f, -Mathf.Clamp(size * 0.05f, 2f, 7f));

            _baseSize = size;
            _followAccent = !primary.HasValue;
            ApplyColors(primary ?? UITheme.Accent, secondary ?? UITheme.AccentAlt);
            Fit();
            _nextFlicker = Random.Range(2.5f, 6f);
            _time = Random.Range(0f, 10f);
        }

        private Text MakeText(RectTransform root, string name, string text, int size, TextAnchor align)
        {
            Text t = UIFactory.LabelLine(root, text, size, UITheme.TextPrimary, align, UIFont.Title, TextGlow.None, false);
            t.name = name;
            UIFactory.Stretch(t.rectTransform);
            return t;
        }

        private void ApplyColors(Color primary, Color secondary)
        {
            if (_main == null)
            {
                return;
            }

            _main.color = UITheme.Lighten(primary, 0.78f);
            _halo.effectColor = UITheme.Alpha(primary, 0.55f);
            _ghostA.color = UITheme.Alpha(secondary, 0.55f);
            _ghostB.color = UITheme.Alpha(primary, 0.5f);
            _plate.color = UITheme.Alpha(Color.Lerp(primary, secondary, 0.35f), 0.2f);
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            UITheme.AccentChanged += OnAccentChanged;
            if (_main != null && _followAccent)
            {
                OnAccentChanged();
            }
        }

        protected override void OnDisable()
        {
            UITheme.AccentChanged -= OnAccentChanged;
            base.OnDisable();
        }

        protected override void OnRectTransformDimensionsChange()
        {
            base.OnRectTransformDimensionsChange();
            Fit();
        }

        /// <summary>Shrinks the font (never below half the design size) until the text fits the rect width.</summary>
        private void Fit()
        {
            if (_main == null || _fitting || _baseSize <= 0)
            {
                return;
            }

            float width = ((RectTransform)transform).rect.width;
            if (width <= 1f)
            {
                return;
            }

            _fitting = true;
            int size = _baseSize;
            if (_main.fontSize != size)
            {
                _main.fontSize = size;
            }

            float preferred = _main.preferredWidth;
            float room = width - _ghostOffset * 2.5f;
            if (preferred > room && preferred > 0f)
            {
                size = Mathf.Max(_baseSize / 2, Mathf.FloorToInt(_baseSize * room / preferred));
            }

            _main.fontSize = size;
            _ghostA.fontSize = size;
            _ghostB.fontSize = size;
            _fitting = false;
        }

        private void OnAccentChanged()
        {
            if (_followAccent)
            {
                ApplyColors(UITheme.Accent, UITheme.AccentAlt);
            }
        }

        private void Update()
        {
            if (_main == null || !_animate)
            {
                return;
            }

            float dt = Time.unscaledDeltaTime;
            _time += dt;
            bool reduced = UITheme.ReducedMotion;

            float breathe = 0.5f + 0.5f * Mathf.Sin(_time * 1.7f);
            float plate = Mathf.Lerp(0.55f, 1f, breathe);
            float ghost = Mathf.Lerp(0.7f, 1f, breathe);
            float core = 1f;

            if (!reduced)
            {
                _nextFlicker -= dt;
                if (_nextFlicker <= 0f && _flickerT < 0f)
                {
                    _flickerT = 0f;
                    _nextFlicker = Random.Range(3.5f, 8f);
                }

                if (_flickerT >= 0f)
                {
                    _flickerT += dt;
                    // Two quick dips then a recovery over ~0.32 s.
                    float f = _flickerT;
                    float dip = f < 0.05f ? 0.35f : (f < 0.1f ? 1f : (f < 0.14f ? 0.55f : (f < 0.32f ? Mathf.Lerp(0.8f, 1f, (f - 0.14f) / 0.18f) : 1f)));
                    core = dip;
                    plate *= dip;
                    ghost *= dip;
                    if (f >= 0.32f)
                    {
                        _flickerT = -1f;
                    }
                }

                float wobble = _ghostOffset * (1f + 0.25f * Mathf.Sin(_time * 2.3f));
                _ghostA.rectTransform.anchoredPosition = new Vector2(-wobble, 0f);
                _ghostB.rectTransform.anchoredPosition = new Vector2(wobble, 0f);
            }
            else
            {
                _ghostA.rectTransform.anchoredPosition = new Vector2(-_ghostOffset, 0f);
                _ghostB.rectTransform.anchoredPosition = new Vector2(_ghostOffset, 0f);
            }

            _main.canvasRenderer.SetAlpha(core);
            ApplyAlpha(core, plate, ghost);
        }

        private void ApplyAlpha(float core, float plate, float ghost)
        {
            if (_main == null)
            {
                return;
            }

            _main.canvasRenderer.SetAlpha(core);
            _plate.canvasRenderer.SetAlpha(plate);
            _ghostA.canvasRenderer.SetAlpha(ghost);
            _ghostB.canvasRenderer.SetAlpha(ghost);
        }
    }
}

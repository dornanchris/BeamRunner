using BeamRunner.Core;
using BeamRunner.Core.Localization;
using BeamRunner.Game;
using UnityEngine;
using UnityEngine.UI;

namespace BeamRunner.UI
{
    /// <summary>
    /// Giant 3 · 2 · 1 · GO! over the field: each number slams in with a springy scale-pop, a glow bloom and an
    /// expanding neon ring, coloured through the current level palette. Used for run starts and (faster) resume
    /// countdowns. Has no raycast targets, so it never blocks steering.
    /// </summary>
    public sealed class CountdownOverlay : UIScreen
    {
        private GameController _game;
        private RectTransform _stage;
        private RectTransform _numberRoot;
        private CanvasGroup _numberGroup;
        private Text _number;
        private Outline _halo;
        private Image _glow;
        private Image _ring;
        private Image _ring2;
        private Text _caption;
        private CanvasGroup _captionGroup;
        private float _step = GameController.CountdownStep;
        private int _hideTween;

        /// <summary>Connects the overlay to the controller (call once, right after creation).</summary>
        public void Bind(GameController game)
        {
            _game = game;
        }

        protected override void Build()
        {
            Transition = ScreenTransition.None;

            _stage = UIFactory.CreateRect(Rect, "Stage");
            UIFactory.Anchor(_stage, AnchorPreset.Center, new Vector2(0f, 140f));
            UIFactory.SetSize(_stage, 800f, 600f);

            _glow = UIFactory.CreateImage(_stage, UISprites.Glow, Color.white, "Glow");
            UIFactory.Anchor(_glow.rectTransform, AnchorPreset.Center);
            UIFactory.SetSize(_glow.rectTransform, 820f, 820f);

            _ring = UIFactory.CreateImage(_stage, UISprites.Ring(8), Color.white, "Ring");
            UIFactory.Anchor(_ring.rectTransform, AnchorPreset.Center);
            UIFactory.SetSize(_ring.rectTransform, 420f, 420f);

            _ring2 = UIFactory.CreateImage(_stage, UISprites.Ring(4), Color.white, "Ring2");
            UIFactory.Anchor(_ring2.rectTransform, AnchorPreset.Center);
            UIFactory.SetSize(_ring2.rectTransform, 420f, 420f);

            _numberRoot = UIFactory.CreateRect(_stage, "Number");
            UIFactory.Anchor(_numberRoot, AnchorPreset.Center);
            UIFactory.SetSize(_numberRoot, 800f, 400f);
            _numberGroup = RunUi.PassiveGroup(_numberRoot);

            _number = UIFactory.LabelLine(_numberRoot, "3", 300, Color.white, TextAnchor.MiddleCenter, UIFont.Title);
            UIFactory.Stretch(_number.rectTransform);
            _halo = _number.gameObject.AddComponent<Outline>();
            _halo.effectDistance = new Vector2(7f, -7f);
            _halo.useGraphicAlpha = true;
            Shadow drop = _number.gameObject.AddComponent<Shadow>();
            drop.effectColor = new Color(0f, 0f, 0.03f, 0.7f);
            drop.effectDistance = new Vector2(0f, -12f);

            _caption = UIFactory.LabelLine(_stage, Loc.T("countdown.get_ready"), UITheme.TextLead, UITheme.TextSecondary, TextAnchor.MiddleCenter,
                UIFont.Bold, TextGlow.Shadow);
            UIFactory.Anchor(_caption.rectTransform, AnchorPreset.Center, new Vector2(0f, -250f));
            _captionGroup = RunUi.PassiveGroup(_caption);

            SetAlpha(_glow, 0f);
            SetAlpha(_ring, 0f);
            SetAlpha(_ring2, 0f);
            _numberGroup.alpha = 0f;
            _captionGroup.alpha = 0f;
            RunUi.StripDecorRaycasts(Rect);
        }

        /// <summary>Prepares a countdown with <paramref name="step"/> seconds per number; <paramref name="resume"/> changes the caption.</summary>
        public void Begin(float step, bool resume)
        {
            EnsureBuilt(); // created deferred (GameUI): the fields below exist once built
            _step = Mathf.Max(0.2f, step);
            UITween.KillId(_hideTween);
            _hideTween = 0;
            _caption.text = resume ? Loc.T("countdown.resuming") : Loc.T("countdown.get_ready");
            _numberGroup.alpha = 0f;
            _captionGroup.alpha = 0f;
            if (!IsVisible)
            {
                Show(true);
            }
        }

        /// <summary>Shows 3, 2, 1 or (0) GO!.</summary>
        public void Tick(int value)
        {
            if (!IsVisible)
            {
                Show(true);
            }

            UITween.KillId(_hideTween);
            _hideTween = 0;

            Color c = PaletteColor(value);
            bool go = value <= 0;
            _number.text = go ? Loc.T("countdown.go") : RunUi.Small(value);
            _number.fontSize = go ? 250 : 300;
            _number.color = UITheme.Lighten(c, go ? 0.7f : 0.55f);
            _halo.effectColor = UITheme.Alpha(c, 0.85f);
            _glow.color = UITheme.Alpha(c, go ? 0.55f : 0.4f);
            _ring.color = UITheme.Alpha(c, 0.95f);
            _ring2.color = UITheme.Alpha(UITheme.Lighten(c, 0.4f), 0.6f);

            float life = go ? 0.6f : _step;
            bool reduced = UITheme.ReducedMotion;

            _numberGroup.alpha = 1f;
            UITween.Scale(_numberRoot, reduced ? 1.2f : (go ? 2.8f : 2.2f), 1f, Mathf.Min(0.42f, life * 0.75f)).SetEase(UIEase.OutBack);
            UITween.Fade(_numberGroup, 1f, 0f, life * 0.32f).SetDelay(life * 0.66f).SetEase(UIEase.InQuad);

            _glow.rectTransform.localScale = Vector3.one;
            UITween.Scale(_glow.rectTransform, 0.5f, go ? 1.5f : 1.1f, life * 0.8f).SetEase(UIEase.OutCubic);
            UITween.Alpha(_glow, 0f, life).SetEase(UIEase.InQuad);

            UITween.Scale(_ring.rectTransform, 0.55f, go ? 2.6f : 1.7f, life * 0.9f).SetEase(UIEase.OutCubic);
            UITween.Alpha(_ring, 0f, life * 0.9f).SetEase(UIEase.OutQuad);
            UITween.Scale(_ring2.rectTransform, 0.4f, go ? 2.1f : 1.35f, life).SetDelay(0.05f).SetEase(UIEase.OutCubic);
            UITween.Alpha(_ring2, 0f, life).SetDelay(0.05f).SetEase(UIEase.OutQuad);

            if (value == 3)
            {
                _captionGroup.alpha = 0f;
                UITween.Fade(_captionGroup, 0f, 1f, 0.25f).SetEase(UIEase.OutCubic);
            }
            else if (go)
            {
                UITween.Fade(_captionGroup, 0f, 0.15f).SetEase(UIEase.InQuad);
            }

            if (go)
            {
                _hideTween = UITween.Delay(this, life + 0.05f, HideNow).Id;
            }
        }

        /// <summary>Hides the overlay immediately (pause, crash, quit).</summary>
        public void HideNow()
        {
            UITween.KillId(_hideTween);
            _hideTween = 0;
            if (IsVisible)
            {
                Hide(true);
            }
        }

        private Color PaletteColor(int value)
        {
            ZoneTheme theme = _game != null ? _game.CurrentTheme : ZoneThemes.Get(0);
            switch (value)
            {
                case 3:
                    return UITheme.ToColor(theme.Primary);
                case 2:
                    return UITheme.ToColor(theme.Secondary);
                case 1:
                    return UITheme.ToColor(theme.Accent);
                default:
                    return Color.Lerp(UITheme.ToColor(theme.Primary), Color.white, 0.25f);
            }
        }

        private static void SetAlpha(Graphic g, float a)
        {
            Color c = g.color;
            c.a = a;
            g.color = c;
        }
    }
}

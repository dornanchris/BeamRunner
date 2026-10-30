using BeamRunner.Core;
using BeamRunner.Core.Field;
using BeamRunner.Core.Localization;
using BeamRunner.Core.Sim;
using BeamRunner.Game;
using BeamRunner.Services;
using UnityEngine;
using UnityEngine.UI;

namespace BeamRunner.UI
{
    /// <summary>
    /// Boot screen: the splash art (the ship flying at a gate's glowing opening, <c>Resources/BeamRunner/Splash</c>) fills
    /// the whole screen and slowly pushes in; the BEAMRUNNER neon logo flickers on over its starry sky at the top, a
    /// thin progress bar follows <see cref="GameController.BootProgress"/> and a tip line cycles every 2.5 s over its grid
    /// floor at the bottom. Without the art the synthwave backdrop shows instead. <see cref="GameUI"/> fades the screen
    /// out into the main menu once boot completes; the art's memory is released then.
    /// </summary>
    public sealed class SplashScreen : UIScreen
    {
        private const float TipInterval = 2.5f;

        /// <summary>Resources path of the splash art (made by <c>Art/Source~/make_splash.py</c>).</summary>
        private const string ArtResource = "BeamRunner/Splash/SplashArt";

        /// <summary>The art's slow push-in: zoom at the end, and how long it takes (boot is usually much shorter).</summary>
        private const float ArtZoom = 1.08f;

        private const float ArtZoomSeconds = 14f;

        /// <summary>The point (UV) the push-in closes on: the vanishing point of the rails, under the sun.</summary>
        private static readonly Vector2 ArtFocus = new Vector2(0.5f, 0.535f);

        private static readonly Vector3[] Corners = new Vector3[4];

        private static readonly string[] TipKeys =
        {
            "tip.near_miss",
            "tip.ramps",
            "tip.fuel",
            "tip.overload",
            "tip.ghost",
            "tip.chain",
            "tip.shield",
            "tip.magnet",
            "tip.missions",
            "tip.levels",
            "tip.start_level",
        };

        private GameController _game;
        private RectTransform _backdrop;
        private RectTransform _artFrame;
        private CanvasGroup _artGroup;
        private RawImage _art;
        private Texture2D _artTexture;
        private RectTransform _screenRoot;
        private float _artZoom = 1f;
        private NeonTitle _title;
        private CanvasGroup _titleGroup;
        private CanvasGroup _subGroup;
        private ProgressBar _bar;
        private CanvasGroup _barGroup;
        private Text _tip;
        private CanvasGroup _tipGroup;
        private float _progressShown;
        private float _tipTimer;
        private int _tipIndex;

        /// <summary>Connects the screen to the controller (call once, right after creation).</summary>
        public void Bind(GameController game)
        {
            _game = game;
        }

        protected override void Build()
        {
            Transition = ScreenTransition.Fade;
            TransitionDuration = 0.8f;

            _backdrop = UIFactory.Backdrop(Rect, 1f, true);
            UIFactory.Stretch(_backdrop, -RunUi.Overscan, -RunUi.Overscan, -RunUi.Overscan, -RunUi.Overscan);
            BuildArt();

            // The logo sits on the art's starry sky, the progress bar and tips on its grid floor.
            RectTransform column = UIFactory.ContentColumn(Rect, UITheme.ContentMaxWidth, UITheme.ScreenPadding);

            _title = UIFactory.NeonTitle(column, "BEAMRUNNER", 132); // l10n-ok: brand
            _title.SetFont(UIFonts.For(LocScript.Latin, UIFont.Title));
            UIFactory.Anchor(_title.Rect, AnchorPreset.StretchTop, new Vector2(0f, -190f));
            _titleGroup = RunUi.PassiveGroup(_title);

            Text sub = UIFactory.LabelLine(column, Loc.T("splash.tagline"), UITheme.TextSmall, UITheme.TextSecondary,
                TextAnchor.MiddleCenter, UIFont.Bold, TextGlow.Shadow);
            UIFactory.Anchor(sub.rectTransform, AnchorPreset.StretchTop, new Vector2(0f, -380f));
            _subGroup = RunUi.PassiveGroup(sub);

            _bar = UIFactory.ProgressBar(column, 0f, 10f, UITheme.AccentCyan, UITheme.AccentMagenta);
            UIFactory.Anchor(_bar.Rect, AnchorPreset.BottomCenter, new Vector2(0f, 290f));
            UIFactory.SetSize(_bar.Rect, 560f, 10f);
            _barGroup = RunUi.PassiveGroup(_bar);

            _tip = UIFactory.Label(column, Tip(0), UITheme.TextBody, UITheme.TextSecondary, TextAnchor.UpperCenter, UIFont.Body, TextGlow.Shadow);
            UIFactory.Anchor(_tip.rectTransform, AnchorPreset.StretchBottom, new Vector2(0f, 120f));
            UIFactory.SetSize(_tip.rectTransform, -40f, 130f);
            _tipGroup = RunUi.PassiveGroup(_tip);

            string version = PlatformUtil.AppVersion;
            if (!string.IsNullOrEmpty(version))
            {
                Text v = RunUi.Caption(Rect, "v" + version);
                UIFactory.Anchor(v.rectTransform, AnchorPreset.BottomCenter, new Vector2(0f, 32f));
            }
        }

        /// <summary>
        /// The art layer: a full-screen frame (this screen sits in the safe area; <see cref="FitArt"/> stretches the frame
        /// over the notches too) holding the art, with shades at the top and bottom so the text reads over it.
        /// </summary>
        private void BuildArt()
        {
            _artFrame = UIFactory.CreateRect(Rect, "Art");
            _artGroup = _artFrame.gameObject.AddComponent<CanvasGroup>();
            _artGroup.blocksRaycasts = false;
            _artGroup.interactable = false;

            RectTransform image = UIFactory.CreateFill(_artFrame, "Image");
            _art = image.gameObject.AddComponent<RawImage>();
            _art.raycastTarget = false;

            Shade("TopShade", 0.66f, 1f, 0.72f, 0f);
            Shade("BottomShade", 0f, 0.3f, 0f, 0.82f);
        }

        /// <summary>A vertical background-coloured gradient over the art between two heights (0 = bottom, 1 = top).</summary>
        private void Shade(string name, float from, float to, float alphaBottom, float alphaTop)
        {
            Image shade = UIFactory.CreateImage(_artFrame, null, Color.white, name);
            RectTransform rt = shade.rectTransform;
            rt.anchorMin = new Vector2(0f, from);
            rt.anchorMax = new Vector2(1f, to);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            Color top = UITheme.Background;
            Color bottom = UITheme.Background;
            top.a = alphaTop;
            bottom.a = alphaBottom;
            UIGradient.Apply(shade, top, bottom, GradientDirection.Vertical);
        }

        protected override void OnShow()
        {
            ShowArt();
            _progressShown = 0f;
            _bar.SetValue(0f, false);
            _tipIndex = Random.Range(0, TipKeys.Length);
            _tip.text = Tip(_tipIndex);
            _tipTimer = TipInterval;

            // Neon "flicker on": the tube stutters a few times before it holds.
            _titleGroup.alpha = 0f;
            _subGroup.alpha = 0f;
            _barGroup.alpha = 0f;
            _tipGroup.alpha = 0f;
            UITween.Value(this, 0f, 1f, 1.1f, FlickerStep).SetDelay(0.15f);
            UITween.Scale(_title.Rect, 1.06f, 1f, 1.4f).SetDelay(0.15f).SetEase(UIEase.OutCubic);
            UITween.Fade(_subGroup, 0f, 1f, 0.5f).SetDelay(1.0f).SetEase(UIEase.OutCubic);
            UITween.Fade(_barGroup, 0f, 1f, 0.4f).SetDelay(0.7f).SetEase(UIEase.OutCubic);
            UITween.Fade(_tipGroup, 0f, 1f, 0.5f).SetDelay(1.1f).SetEase(UIEase.OutCubic);
        }

        protected override void OnHide()
        {
            UITween.Kill(this);
        }

        protected override void OnHidden()
        {
            // The art is only needed while booting: give its memory back (OnShow loads it again).
            _art.texture = null;
            if (_artTexture != null)
            {
                Resources.UnloadAsset(_artTexture);
                _artTexture = null;
            }
        }

        /// <summary>Loads the art and starts its fade-in and push-in; without it, the backdrop stays.</summary>
        private void ShowArt()
        {
            if (_artTexture == null)
            {
                _artTexture = Resources.Load<Texture2D>(ArtResource);
            }

            _art.texture = _artTexture;
            bool hasArt = _artTexture != null;
            _artFrame.gameObject.SetActive(hasArt);
            _backdrop.gameObject.SetActive(true);
            if (!hasArt)
            {
                return;
            }

            _artZoom = 1f;
            FitArt();
            _artGroup.alpha = 0f;
            UITween.Fade(_artGroup, 0f, 1f, 0.45f).SetEase(UIEase.OutQuad).OnComplete(HideBackdrop);
            UITween.Value(this, 1f, ArtZoom, ArtZoomSeconds, SetArtZoom).SetEase(UIEase.OutSine);
        }

        /// <summary>The opaque art covers the backdrop once it has faded in: stop drawing it.</summary>
        private void HideBackdrop()
        {
            if (_backdrop != null && _art != null && _art.texture != null)
            {
                _backdrop.gameObject.SetActive(false);
            }
        }

        private void SetArtZoom(float zoom)
        {
            _artZoom = zoom;
        }

        /// <summary>
        /// Stretches the art frame over the whole screen (the root canvas, measured in this screen's space so any safe-area
        /// inset is undone) and aspect-fills the art through its UV rect: a wider screen crops top and bottom, a taller
        /// one the sides, and the push-in narrows the crop around <see cref="ArtFocus"/>.
        /// </summary>
        private void FitArt()
        {
            if (_artTexture == null || _artTexture.height <= 0)
            {
                return;
            }

            if (_screenRoot == null)
            {
                Canvas canvas = GetComponentInParent<Canvas>();
                _screenRoot = canvas != null ? canvas.rootCanvas.transform as RectTransform : null;
            }

            Vector2 size;
            if (_screenRoot != null)
            {
                _screenRoot.GetWorldCorners(Corners);
                Vector2 min = Rect.InverseTransformPoint(Corners[0]);
                Vector2 max = Rect.InverseTransformPoint(Corners[2]);
                size = max - min;
                Vector2 position = (min + max) * 0.5f - Rect.rect.center;
                if (_artFrame.sizeDelta != size || _artFrame.anchoredPosition != position)
                {
                    _artFrame.anchorMin = new Vector2(0.5f, 0.5f);
                    _artFrame.anchorMax = new Vector2(0.5f, 0.5f);
                    _artFrame.pivot = new Vector2(0.5f, 0.5f);
                    _artFrame.sizeDelta = size;
                    _artFrame.anchoredPosition = position;
                }
            }
            else
            {
                UIFactory.Stretch(_artFrame);
                size = Rect.rect.size;
            }

            if (size.x <= 0f || size.y <= 0f)
            {
                return;
            }

            float frameAspect = size.x / size.y;
            float artAspect = (float)_artTexture.width / _artTexture.height;
            float w0 = frameAspect > artAspect ? 1f : frameAspect / artAspect;
            float h0 = frameAspect > artAspect ? artAspect / frameAspect : 1f;
            float w = w0 / _artZoom;
            float h = h0 / _artZoom;

            // The focus keeps its place in the frame while the crop narrows around it.
            float x = ArtFocus.x - (ArtFocus.x - (1f - w0) * 0.5f) / w0 * w;
            float y = ArtFocus.y - (ArtFocus.y - (1f - h0) * 0.5f) / h0 * h;
            var uv = new UnityEngine.Rect(Mathf.Clamp(x, 0f, 1f - w), Mathf.Clamp(y, 0f, 1f - h), w, h);
            if (_art.uvRect != uv)
            {
                _art.uvRect = uv;
            }
        }

        private void FlickerStep(float t)
        {
            float a;
            if (t < 0.08f)
            {
                a = 0f;
            }
            else if (t < 0.13f)
            {
                a = 0.85f;
            }
            else if (t < 0.22f)
            {
                a = 0.05f;
            }
            else if (t < 0.27f)
            {
                a = 0.6f;
            }
            else if (t < 0.33f)
            {
                a = 0.12f;
            }
            else if (t < 0.4f)
            {
                a = 1f;
            }
            else if (t < 0.44f)
            {
                a = 0.35f;
            }
            else
            {
                a = Mathf.Lerp(0.7f, 1f, Mathf.Clamp01((t - 0.44f) / 0.3f));
            }

            if (_titleGroup != null)
            {
                _titleGroup.alpha = a;
            }
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (_art != null && _art.texture != null)
            {
                FitArt();
            }

            float target = _game != null ? _game.BootProgress : 0f;
            if (!Mathf.Approximately(target, _progressShown))
            {
                _progressShown = Mathf.MoveTowards(_progressShown, target, Mathf.Max(0.6f * dt, (target - _progressShown) * dt * 6f));
                _bar.SetValue(_progressShown, false);
            }

            _tipTimer -= dt;
            if (_tipTimer <= 0f && IsVisible)
            {
                _tipTimer = TipInterval;
                NextTip();
            }
        }

        private void NextTip()
        {
            UITween.Fade(_tipGroup, 0f, 0.22f).SetEase(UIEase.InQuad).OnComplete(ShowNextTip);
        }

        private void ShowNextTip()
        {
            if (_tip == null)
            {
                return;
            }

            _tipIndex = (_tipIndex + 1) % TipKeys.Length;
            _tip.text = Tip(_tipIndex);
            UITween.Fade(_tipGroup, 1f, 0.3f).SetEase(UIEase.OutCubic);
        }

        private static string Tip(int index)
        {
            switch (TipKeys[Mathf.Clamp(index, 0, TipKeys.Length - 1)])
            {
                case "tip.chain":
                    return Loc.T("tip.chain", ("n", RunSimulation.MaxMultiplier));
                case "tip.levels":
                    return Loc.T("tip.levels", ("n", Levels.PlateauLevel));
                default:
                    return Loc.T(TipKeys[Mathf.Clamp(index, 0, TipKeys.Length - 1)]);
            }
        }
    }
}

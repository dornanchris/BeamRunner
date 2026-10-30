using System.Collections;
using System.Collections.Generic;
using BeamRunner.Core.Field;
using BeamRunner.Core.Localization;
using BeamRunner.Core.Sim;
using BeamRunner.Game;
using UnityEngine;
using UnityEngine.UI;

namespace BeamRunner.UI
{
    /// <summary>
    /// In-run HUD. Top: pause (left), score with "LEVEL n · distance" and a thin level-progress bar under it (centre),
    /// gem pill (right), multiplier badge with four chain pips and a decay bar, power-up chips (shield; magnet, Overload
    /// and instant build with radial timers). Bottom: the hull (one heart per point, left) above the fuel gauge (glass
    /// track, green fill, "FUEL n / max"; amber below the price of a bridge, red and shaking on an empty build, "FREE
    /// BUILDS" during instant build). Event feedback (popups, banners, tutorial hints) lives in <c>HudScreen.Fx.cs</c>.
    /// <para>Only buttons are raycast targets, so taps and swipes anywhere else steer and build. Per-frame work is
    /// change-detected: strings are rebuilt only when a displayed integer changes, and fast-changing parts sit on nested
    /// canvases.</para>
    /// </summary>
    public sealed partial class HudScreen : UIScreen
    {
        private const float Margin = 28f;
        private const float PauseSize = 112f;
        private const float ChipSize = 108f;
        private const int PipCount = 4;
        private const float HeartSize = 54f;

        private static readonly Color FuelColor = new Color(0.35f, 1f, 0.45f);
        private static readonly Color FuelColorAlt = new Color(0.2f, 0.85f, 1f);

        private GameController _game;
        private RunSimulation _boundRun;
        private bool _layoutSubscribed;

        private RectTransform _top;
        private RectTransform _bottom;

        // Top.
        private NeonButton _pause;
        private RectTransform _scoreRoot;
        private Text _score;
        private Text _distance;
        private ProgressBar _levelBar;

        private CurrencyPill _gems;
        private RectTransform _multRoot;
        private CanvasGroup _multGroup;
        private Text _multText;
        private Image _multGlow;
        private readonly Image[] _pipOn = new Image[PipCount];
        private readonly RectTransform[] _pipRoots = new RectTransform[PipCount];
        private RectTransform _decayFill;
        private RectTransform _chips;
        private RectTransform _shieldChip;
        private TimedChip _magnetChip;
        private TimedChip _overloadChip;
        private TimedChip _instantChip;

        /// <summary>A power-up chip with a ring that runs down with the power-up (magnet, Overload, instant build).</summary>
        private sealed class TimedChip
        {
            public RectTransform Rect;
            public CanvasGroup Group;
            public RingProgress Ring;
            public bool On;
            public float Shown = -1f;
        }

        // Bottom.
        private RectTransform _meterRoot;
        private RectTransform _meterTrack;
        private ProgressBar _meter;
        private Image _meterFrame;
        private Image _meterIcon;
        private Text _meterLabel;
        private Text _fuelValue;
        private RectTransform _hullRoot;
        private readonly List<Image> _hearts = new List<Image>(8);

        // Displayed state (change detection).
        private double _scoreShown;
        private long _scoreDisplayed = -1;
        private long _distanceDisplayed = -1;
        private int _levelDisplayed = -1;
        private LanguageInfo _levelLanguage;
        private string _levelPrefix;
        private string _distanceSuffix;
        private float _levelProgressShown = -1f;

        private int _gemsDisplayed = -1;
        private int _multDisplayed = -1;
        private int _chainDisplayed = -1;
        private bool _multIdle;
        private float _decayShown = -1f;
        private bool _shieldOn;

        private float _meterShown = -1f;
        private int _fuelDisplayed = -1;
        private int _fuelMaxDisplayed = -1;
        private int _fuelState = -1;
        private int _hullDisplayed = -1;
        private int _hullMaxDisplayed = -1;
        private float _emptyFlash;
        private float _time;
        private bool _dimmed;

        /// <summary>Connects the HUD to the controller (call once, right after creation).</summary>
        public void Bind(GameController game)
        {
            _game = game;
            UIRoot root = UIRoot.Instance;
            if (root != null && !_layoutSubscribed)
            {
                root.LayoutChanged += Relayout;
                _layoutSubscribed = true;
            }
        }

        /// <summary>True while the HUD is dimmed/faded (pause, crash).</summary>
        public bool IsDimmed
        {
            get { return _dimmed; }
        }

        /// <summary>Dims (non-interactive, faded to <paramref name="alpha"/>) or restores the HUD.</summary>
        public void SetDimmed(bool dim, float alpha = 0.3f, float duration = 0.25f)
        {
            if (!IsVisible)
            {
                if (!dim)
                {
                    Show();
                }

                return;
            }

            _dimmed = dim;
            CanvasGroup g = Group;
            g.interactable = !dim;
            g.blocksRaycasts = !dim;
            UITween.Fade(g, dim ? Mathf.Clamp01(alpha) : 1f, duration).SetEase(UIEase.OutCubic);
        }

        /// <summary>Re-reads settings that affect the layout.</summary>
        public void RefreshSettings()
        {
            Relayout();
        }

        // Three steps for the build queue (UIScreen.BuildSteps). Relayout (LayoutChanged) may run between them: it waits
        // for _meterRoot, which BuildBottom creates.
        protected override IEnumerator BuildSteps()
        {
            Transition = ScreenTransition.Fade;
            TransitionDuration = 0.35f;

            BuildFx();

            _top = UIFactory.CreateRect(Rect, "Top");
            UIFactory.Anchor(_top, AnchorPreset.StretchTop);
            UIFactory.SetSize(_top, 0f, 320f);
            _bottom = UIFactory.CreateRect(Rect, "Bottom");
            UIFactory.Anchor(_bottom, AnchorPreset.StretchBottom);
            UIFactory.SetSize(_bottom, 0f, 300f);

            BuildTop();
            yield return null;
            BuildChips();
            BuildGhostChip(); // Daily Run ghost gap (HudScreen.Ghost.cs)
            BuildBottom();

            yield return null;
            BuildHint();

            // Touch/mouse-only button (keyboard and gamepad have a direct pause binding). With the default Automatic
            // navigation a mouse click leaves it *selected*, and Enter / gamepad A (Submit) would pause mid-run.
            var noNavigation = new Navigation { mode = Navigation.Mode.None };
            _pause.navigation = noNavigation;

            RunUi.StripDecorRaycasts(Rect);
            if (FootageMode.Enabled)
            {
                // Background mode: nothing on screen but the ride and a small watermark (clean for captions).
                _top.gameObject.SetActive(false);
                _bottom.gameObject.SetActive(false);
                _fx.gameObject.SetActive(false);
                Text mark = UIFactory.LabelLine(Rect, "BEAMRUNNER", 34, UITheme.Alpha(UITheme.TextPrimary, 0.45f), // l10n-ok: brand watermark
                    TextAnchor.MiddleRight, UIFont.Title, TextGlow.Shadow);
                UIFactory.Anchor(mark.rectTransform, AnchorPreset.BottomRight, new Vector2(-Margin, Margin + 40f));
                mark.rectTransform.sizeDelta = new Vector2(600f, 60f);
                mark.raycastTarget = false;
            }
        }

        private void BuildTop()
        {
            _pause = UIFactory.IconButton(_top, IconId.Pause, OnPause, PauseSize, ButtonStyle.Secondary);
            UIFactory.Anchor(_pause.Rect, AnchorPreset.TopLeft, new Vector2(Margin, -Margin));

            // Score + distance (own canvas: their text changes nearly every frame).
            _scoreRoot = UIFactory.CreateRect(_top, "Score");
            UIFactory.Anchor(_scoreRoot, AnchorPreset.TopCenter, new Vector2(0f, -6f));
            UIFactory.SetSize(_scoreRoot, 640f, 186f);
            _scoreRoot.gameObject.AddComponent<Canvas>();
            _score = UIFactory.LabelLine(_scoreRoot, "0", 112, UITheme.TextPrimary, TextAnchor.MiddleCenter, UIFont.Title, TextGlow.Neon);
            UIFactory.Anchor(_score.rectTransform, AnchorPreset.StretchTop);
            UIFactory.SetSize(_score.rectTransform, 0f, 126f);
            _distance = UIFactory.LabelLine(_scoreRoot, Loc.T("hud.level", ("n", 1)), 40, UITheme.TextSecondary, TextAnchor.MiddleCenter, UIFont.Bold, TextGlow.Shadow);
            UIFactory.Anchor(_distance.rectTransform, AnchorPreset.StretchBottom, new Vector2(0f, 16f));
            UIFactory.SetSize(_distance.rectTransform, 0f, 46f);

            // Progress through the current level.
            _levelBar = UIFactory.ProgressBar(_scoreRoot, 0f, 8f, UITheme.AccentCyan, UITheme.AccentMagenta);
            UIFactory.Anchor(_levelBar.Rect, AnchorPreset.BottomCenter, new Vector2(0f, 4f));
            UIFactory.SetSize(_levelBar.Rect, 360f, 8f);

            // Gems.
            _gems = UIFactory.CurrencyPill(_top, 0);
            UIFactory.Anchor(_gems.Rect, AnchorPreset.TopRight, new Vector2(-Margin, -Margin - 12f));

            // Multiplier badge: "×3" + chain pips + decay bar.
            const float badgeW = 300f;
            const float badgeH = 84f;
            _multRoot = UIFactory.CreateRect(_top, "Multiplier");
            UIFactory.Anchor(_multRoot, AnchorPreset.TopCenter, new Vector2(0f, -200f));
            UIFactory.SetSize(_multRoot, badgeW, badgeH);
            _multRoot.gameObject.AddComponent<Canvas>();
            _multGroup = RunUi.PassiveGroup(_multRoot);

            _multGlow = UIFactory.CreateImage(_multRoot, UISprites.Glow, UITheme.Accent, "Glow");
            UIFactory.Stretch(_multGlow.rectTransform, -60f, -50f, -60f, -50f);
            UIAccentFollower.Attach(_multGlow, AccentSlot.Primary, 0.9f);
            _multGlow.canvasRenderer.SetAlpha(0f);

            UIFactory.PillImage(_multRoot, UITheme.PanelGlass, "Background");
            Image frame = UIFactory.CreateImage(_multRoot, null, UITheme.Accent, "Frame");
            UIFactory.Stretch(frame.rectTransform);
            UISprites.ApplyOutline(frame, badgeH * 0.5f, 3f);
            UIAccentFollower.Attach(frame, AccentSlot.Primary, 0.85f);

            _multText = UIFactory.LabelLine(_multRoot, RunUi.Multiplier(1), 54, UITheme.TextPrimary, TextAnchor.MiddleLeft, UIFont.Title, TextGlow.Neon);
            UIFactory.Anchor(_multText.rectTransform, AnchorPreset.MiddleLeft, new Vector2(30f, 4f));
            UIFactory.SetSize(_multText.rectTransform, 120f, 70f);

            for (int i = 0; i < PipCount; i++)
            {
                RectTransform pip = UIFactory.CreateRect(_multRoot, "Pip" + i);
                UIFactory.Anchor(pip, AnchorPreset.MiddleLeft, new Vector2(150f + i * 32f, 4f));
                UIFactory.SetSize(pip, 22f, 22f);
                Image off = UIFactory.CreateImage(pip, UISprites.Circle, new Color(1f, 1f, 1f, 0.16f), "Off");
                UIFactory.Stretch(off.rectTransform);
                Image on = UIFactory.CreateImage(pip, UISprites.Circle, UITheme.AccentAlt, "On");
                UIFactory.Stretch(on.rectTransform, -2f, -2f, -2f, -2f);
                UIAccentFollower.Attach(on, AccentSlot.Secondary, 1f, 0.2f);
                on.enabled = false;
                _pipOn[i] = on;
                _pipRoots[i] = pip;
            }

            Image decayTrack = UIFactory.CreateImage(_multRoot, null, UITheme.Track, "DecayTrack");
            RectTransform dt = decayTrack.rectTransform;
            UIFactory.Anchor(dt, AnchorPreset.StretchBottom, new Vector2(0f, 12f));
            UIFactory.SetSize(dt, -76f, 6f);
            UIShape.Rounded(decayTrack, 3f);
            Image decay = UIFactory.CreateImage(dt, null, Color.white, "Decay");
            UIGradient.ApplyAccent(decay, GradientDirection.Horizontal);
            UIShape.Rounded(decay, 3f);
            _decayFill = decay.rectTransform;
            _decayFill.anchorMin = Vector2.zero;
            _decayFill.anchorMax = new Vector2(0f, 1f);
            _decayFill.pivot = new Vector2(0f, 0.5f);
            _decayFill.offsetMin = Vector2.zero;
            _decayFill.offsetMax = Vector2.zero;
        }

        private void BuildChips()
        {
            _chips = UIFactory.CreateRect(_top, "Chips");
            UIFactory.Anchor(_chips, AnchorPreset.TopRight, new Vector2(-Margin - 6f, -Margin - 118f));
            UIFactory.SetSize(_chips, ChipSize + 8f, ChipSize * 4f + 120f);
            _chips.gameObject.AddComponent<Canvas>();
            VerticalLayoutGroup v = UIFactory.VStack(_chips, UITheme.SpaceMd, null, TextAnchor.UpperRight, true);
            v.childForceExpandWidth = false;

            RingProgress unused;
            _shieldChip = BuildChip("ShieldChip", IconId.Shield, UITheme.AccentCyan, false, out unused);
            _shieldChip.gameObject.SetActive(false);
            _magnetChip = BuildTimedChip("MagnetChip", IconId.Magnet, UITheme.AccentPurple);
            _overloadChip = BuildTimedChip("OverloadChip", IconId.Bolt, UITheme.AccentYellow);
            _instantChip = BuildTimedChip("InstantChip", IconId.ChevronUp, UITheme.AccentCyan);
        }

        private TimedChip BuildTimedChip(string name, IconId icon, Color color)
        {
            var chip = new TimedChip();
            chip.Rect = BuildChip(name, icon, color, true, out chip.Ring);
            chip.Group = RunUi.PassiveGroup(chip.Rect);
            chip.Rect.gameObject.SetActive(false);
            return chip;
        }

        private RectTransform BuildChip(string name, IconId icon, Color color, bool timer, out RingProgress timerRing)
        {
            timerRing = null;
            RectTransform chip = UIFactory.CreateRect(_chips, name);
            UIFactory.SetLayout(chip, ChipSize, ChipSize);
            UIFactory.SetSize(chip, ChipSize, ChipSize);

            Image glow = UIFactory.CreateImage(chip, UISprites.Glow, UITheme.Alpha(color, 0.45f), "Glow");
            UIFactory.Stretch(glow.rectTransform, -30f, -30f, -30f, -30f);
            Image bg = UIFactory.CreateImage(chip, UISprites.Circle, UITheme.PanelGlass, "Background");
            UIFactory.Stretch(bg.rectTransform, 6f, 6f, 6f, 6f);
            if (timer)
            {
                timerRing = UIFactory.RingProgress(chip, ChipSize, 1f, 9f, color);
                UIFactory.Anchor(timerRing.Rect, AnchorPreset.Center);
                timerRing.Label.text = string.Empty;
            }
            else
            {
                Image ring = UIFactory.CreateImage(chip, UISprites.Ring(10), color, "Ring");
                UIFactory.Stretch(ring.rectTransform);
            }

            Image ic = UIFactory.Icon(chip, icon, 54f, UITheme.Lighten(color, 0.35f));
            UIFactory.Anchor(ic.rectTransform, AnchorPreset.Center);
            return chip;
        }

        private void BuildBottom()
        {
            // Hull: one heart per point (rebuilt when the hull size changes).
            _hullRoot = UIFactory.CreateRect(_bottom, "Hull");
            UIFactory.SetSize(_hullRoot, 400f, HeartSize + 8f);
            _hullRoot.gameObject.AddComponent<Canvas>();
            HorizontalLayoutGroup h = UIFactory.HStack(_hullRoot, 10f, null, TextAnchor.MiddleLeft, true);
            h.childForceExpandWidth = false;

            // Fuel gauge (own canvas: the fill moves every frame).
            _meterRoot = UIFactory.CreateRect(_bottom, "Fuel");
            UIFactory.SetSize(_meterRoot, 720f, 112f);
            _meterRoot.gameObject.AddComponent<Canvas>();

            _meterIcon = UIFactory.Icon(_meterRoot, IconId.Fuel, 40f, FuelColor);
            UIFactory.Anchor(_meterIcon.rectTransform, AnchorPreset.TopLeft, new Vector2(10f, -4f));
            _meterLabel = UIFactory.LabelLine(_meterRoot, Loc.T("hud.fuel"), 38, UITheme.TextSecondary, TextAnchor.MiddleLeft, UIFont.Bold, TextGlow.Shadow);
            UIFactory.Anchor(_meterLabel.rectTransform, AnchorPreset.TopLeft, new Vector2(62f, 0f));
            UIFactory.SetSize(_meterLabel.rectTransform, 420f, 48f);
            _fuelValue = UIFactory.LabelLine(_meterRoot, string.Empty, 40, UITheme.TextPrimary, TextAnchor.MiddleRight, UIFont.Title, TextGlow.Shadow);
            UIFactory.Anchor(_fuelValue.rectTransform, AnchorPreset.TopRight, new Vector2(-10f, 0f));
            UIFactory.SetSize(_fuelValue.rectTransform, 260f, 48f);

            _meterTrack = UIFactory.CreateRect(_meterRoot, "Track");
            UIFactory.Anchor(_meterTrack, AnchorPreset.StretchBottom);
            UIFactory.SetSize(_meterTrack, 0f, 46f);
            UIFactory.PillImage(_meterTrack, UITheme.PanelGlass, "Glass");
            Image sheen = UIFactory.PillImage(_meterTrack, Color.white, "Sheen");
            UIFactory.Stretch(sheen.rectTransform, 4f, 3f, 4f, 23f);
            UIGradient.Apply(sheen, new Color(1f, 1f, 1f, 0.14f), new Color(1f, 1f, 1f, 0f), GradientDirection.Vertical);
            _meter = UIFactory.ProgressBar(_meterTrack, 0f, 26f, FuelColorAlt, FuelColor);
            UIFactory.Stretch(_meter.Rect, 10f, 10f, 10f, 10f);
            _meterFrame = UIFactory.CreateImage(_meterTrack, null, FuelColor, "Frame");
            UIFactory.Stretch(_meterFrame.rectTransform);
            UISprites.ApplyOutline(_meterFrame, 23f, 3f);
            _meterFrame.canvasRenderer.SetAlpha(0.55f);
        }

        protected override void OnShow()
        {
            _dimmed = false;
            Relayout();
            ResetForRun(_game != null ? _game.Run : null);
            if (!UITheme.ReducedMotion)
            {
                UITween.Move(_top, new Vector2(0f, 240f), Vector2.zero, 0.55f).SetEase(UIEase.OutCubic);
                UITween.Move(_bottom, new Vector2(0f, -300f), Vector2.zero, 0.55f).SetDelay(0.06f).SetEase(UIEase.OutCubic);
            }
            else
            {
                _top.anchoredPosition = Vector2.zero;
                _bottom.anchoredPosition = Vector2.zero;
            }
        }

        protected override void OnHide()
        {
            HideTransientFx();
        }

        private void OnDestroy()
        {
            UIRoot root = UIRoot.Instance;
            if (root != null && _layoutSubscribed)
            {
                root.LayoutChanged -= Relayout;
            }

            _layoutSubscribed = false;
        }

        /// <summary>Fits the fuel gauge to the width (centred, low on the screen) with the hull above its left end.</summary>
        private void Relayout()
        {
            if (_meterRoot == null)
            {
                return;
            }

            float width = Rect.rect.width;
            if (width <= 1f)
            {
                width = UIRoot.ReferenceResolution.x;
            }

            float meterWidth = Mathf.Clamp(width - Margin * 2f - 40f, 360f, 820f);
            UIFactory.Anchor(_meterRoot, AnchorPreset.BottomCenter, new Vector2(0f, Margin + 36f));
            UIFactory.SetSize(_meterRoot, meterWidth, 112f);
            UIFactory.Anchor(_hullRoot, AnchorPreset.BottomCenter, new Vector2(-meterWidth * 0.5f + 200f + 8f, Margin + 160f));
            _meterShown = -1f;
        }

        private void ResetForRun(RunSimulation run)
        {
            _boundRun = run;
            _scoreShown = run != null ? run.Score : 0;
            _scoreDisplayed = -1;
            _distanceDisplayed = -1;
            _levelDisplayed = -1;
            _levelProgressShown = -1f;
            _gemsDisplayed = -1;
            _multDisplayed = -1;
            _chainDisplayed = -1;
            _decayShown = -1f;
            _meterShown = -1f;
            _fuelDisplayed = -1;
            _fuelMaxDisplayed = -1;
            _fuelState = -1;
            _hullDisplayed = -1;
            _hullMaxDisplayed = -1;
            _emptyFlash = 0f;
            _multIdle = false;
            _multGroup.alpha = 1f;
            _multRoot.localScale = Vector3.one;
            _meterRoot.localScale = Vector3.one;

            _shieldOn = false;
            UITween.Kill(_shieldChip);
            _shieldChip.gameObject.SetActive(false);
            _shieldChip.localScale = Vector3.one;
            ResetTimedChip(_magnetChip);
            ResetTimedChip(_overloadChip);
            ResetTimedChip(_instantChip);
            _gems.SetAmount(run != null ? run.Gems : 0, false);
            _gemsDisplayed = run != null ? run.Gems : 0;

            HideTransientFx();
            ResetGhostChip();
            if (run != null)
            {
                UpdateAll(run, 0f);
            }
        }

        private void Update()
        {
            if (_game == null)
            {
                return;
            }

            float dt = Time.unscaledDeltaTime;
            _time += dt;
            RunSimulation run = _game.Run;
            if (!ReferenceEquals(run, _boundRun))
            {
                ResetForRun(run);
            }

            UpdateFx(dt);
            if (run != null)
            {
                UpdateAll(run, dt);
            }
        }

        private void LateUpdate()
        {
            FlushPendingPopup();
        }

        private void UpdateAll(RunSimulation run, float dt)
        {
            UpdateScore(run, dt);
            UpdateMultiplier(run);
            UpdateChips(run);
            UpdateFuel(run, dt);
            UpdateHull(run);
            UpdateGhostChip();
        }

        private static void ResetTimedChip(TimedChip chip)
        {
            chip.On = false;
            chip.Shown = -1f;
            UITween.Kill(chip.Rect);
            chip.Rect.gameObject.SetActive(false);
            chip.Rect.localScale = Vector3.one;
        }

        private void UpdateScore(RunSimulation run, float dt)
        {
            long target = run.Score;
            if (_scoreShown > target || dt <= 0f)
            {
                _scoreShown = target;
            }
            else
            {
                double gap = target - _scoreShown;
                _scoreShown += System.Math.Max(gap * Mathf.Min(1f, dt * 12f), System.Math.Min(gap, 60.0 * dt));
                if (target - _scoreShown < 1.0)
                {
                    _scoreShown = target;
                }
            }

            long shown = (long)_scoreShown;
            if (shown != _scoreDisplayed)
            {
                _scoreDisplayed = shown;
                _score.text = RunUi.Grouped(shown);
            }

            long distance = (long)run.Distance;
            int level = run.Level;
            if (level != _levelDisplayed || !ReferenceEquals(_levelLanguage, Loc.Language))
            {
                // "LEVEL n · " + the distance pattern's lead, once per level (the distance changes every frame).
                _levelDisplayed = level;
                _levelLanguage = Loc.Language;
                string before;
                RunUi.Pattern("hud.distance", out before, out _distanceSuffix);
                _levelPrefix = Loc.T("hud.level", ("n", level)) + RunUi.Dot + before;
                _distanceDisplayed = -1;
            }

            if (distance != _distanceDisplayed)
            {
                _distanceDisplayed = distance;
                _distance.text = RunUi.Grouped(distance, _levelPrefix, _distanceSuffix);
            }

            float progress = run.LevelProgress01;
            if (Mathf.Abs(progress - _levelProgressShown) > 0.004f)
            {
                _levelProgressShown = progress;
                _levelBar.SetValue(progress, false);
            }

            int gems = run.Gems;
            if (gems != _gemsDisplayed)
            {
                _gems.SetAmount(gems, gems > _gemsDisplayed && _gemsDisplayed >= 0);
                _gemsDisplayed = gems;
            }
        }

        private void UpdateMultiplier(RunSimulation run)
        {
            int m = run.Multiplier;
            if (m != _multDisplayed)
            {
                bool up = _multDisplayed > 0 && m > _multDisplayed;
                bool down = _multDisplayed > 0 && m < _multDisplayed;
                _multDisplayed = m;
                _multText.text = RunUi.Multiplier(m);
                if (up)
                {
                    OnMultiplierUp(m);
                }
                else if (down)
                {
                    UITween.Shake(_multText.rectTransform, 8f, 0.3f);
                }
            }

            int chain = Mathf.Clamp(run.ChainProgress * PipCount / RunSimulation.ChainPerMultiplier, 0, PipCount);
            if (chain != _chainDisplayed)
            {
                bool grew = chain > _chainDisplayed && _chainDisplayed >= 0;
                _chainDisplayed = chain;
                for (int i = 0; i < PipCount; i++)
                {
                    _pipOn[i].enabled = i < chain;
                }

                if (grew && chain > 0)
                {
                    UITween.Punch(_pipRoots[chain - 1], 0.6f, 0.35f);
                }
            }

            float decay = run.ChainTimer01;
            if (Mathf.Abs(decay - _decayShown) > 0.004f || (decay <= 0f && _decayShown > 0f))
            {
                _decayShown = decay;
                _decayFill.anchorMax = new Vector2(Mathf.Clamp01(decay), 1f);
                _decayFill.offsetMax = Vector2.zero;
            }

            bool idle = m <= 1 && chain == 0;
            if (idle != _multIdle)
            {
                _multIdle = idle;
                UITween.Fade(_multGroup, idle ? 0.55f : 1f, 0.3f).SetEase(UIEase.OutCubic);
            }
        }

        private void OnMultiplierUp(int m)
        {
            UITween.Punch(_multRoot, 0.35f, 0.45f);
            _multGlow.canvasRenderer.SetAlpha(1f);
            UITween.RendererAlpha(_multGlow, 0f, 0.7f).SetEase(UIEase.OutCubic);
            SpawnAtShip(Loc.T("hud.multiplier_popup", ("mult", RunUi.Multiplier(m))), UITheme.AccentYellow, 330f, 0.8f, false);
        }

        private void UpdateChips(RunSimulation run)
        {
            bool shield = run.ShieldActive;
            if (shield != _shieldOn)
            {
                _shieldOn = shield;
                SetChip(_shieldChip, shield);
            }

            Core.Balance.BalanceConfig b = Core.Balance.BalanceConfig.Current;
            float scale = run.Ship.DurationScale;
            UpdateTimedChip(_magnetChip, run.MagnetRemaining, b.MagnetSeconds * scale);
            UpdateTimedChip(_overloadChip, run.OverloadRemaining, b.OverloadSeconds * scale);
            UpdateTimedChip(_instantChip, run.InstantBuildRemaining, b.InstantBuildSeconds * scale);
        }

        private void UpdateTimedChip(TimedChip chip, float remaining, float duration)
        {
            bool on = remaining > 0f;
            if (on != chip.On)
            {
                chip.On = on;
                SetChip(chip.Rect, on);
                chip.Shown = -1f;
            }

            if (!on)
            {
                return;
            }

            float v = Mathf.Clamp01(remaining / Mathf.Max(0.01f, duration));
            if (Mathf.Abs(v - chip.Shown) > 0.003f)
            {
                chip.Shown = v;
                chip.Ring.SetValue(v, false);
            }

            // Blink during the last two seconds.
            float a = remaining < 2f ? 0.55f + 0.45f * Mathf.Abs(Mathf.Cos(_time * 9f)) : 1f;
            if (!Mathf.Approximately(a, chip.Group.alpha))
            {
                chip.Group.alpha = a;
            }
        }

        private bool ChipOn(RectTransform chip)
        {
            if (chip == _shieldChip)
            {
                return _shieldOn;
            }

            return chip == _magnetChip.Rect ? _magnetChip.On : chip == _overloadChip.Rect ? _overloadChip.On : _instantChip.On;
        }

        private void SetChip(RectTransform chip, bool on)
        {
            UITween.Kill(chip);
            if (on)
            {
                chip.gameObject.SetActive(true);
                UITween.Scale(chip, 0.2f, 1f, 0.45f).SetEase(UIEase.OutBack);
            }
            else if (chip.gameObject.activeSelf)
            {
                UITween.Scale(chip, 0f, 0.2f).SetEase(UIEase.InBack).OnComplete(() =>
                {
                    if (chip != null && !ChipOn(chip))
                    {
                        chip.gameObject.SetActive(false);
                        chip.localScale = Vector3.one;
                    }
                });
            }
        }

        /// <summary>The fuel gauge: fill, "n / max", and its state colour (normal, low, free builds).</summary>
        private void UpdateFuel(RunSimulation run, float dt)
        {
            float max = Mathf.Max(1f, run.MaxFuel);
            float v = Mathf.Clamp01(run.Fuel / max);
            if (Mathf.Abs(v - _meterShown) > 0.002f)
            {
                _meterShown = v;
                _meter.SetValue(v, false);
            }

            int fuel = Mathf.FloorToInt(run.Fuel + 1e-3f);
            int maxInt = Mathf.RoundToInt(max);
            if (fuel != _fuelDisplayed || maxInt != _fuelMaxDisplayed)
            {
                if (fuel > _fuelDisplayed && _fuelDisplayed >= 0)
                {
                    UITween.Punch(_fuelValue.rectTransform, 0.25f, 0.3f);
                }

                _fuelDisplayed = fuel;
                _fuelMaxDisplayed = maxInt;
                _fuelValue.text = Loc.T("hud.fuel_value", ("fuel", RunUi.Small(fuel)), ("max", RunUi.Small(maxInt)));
            }

            bool free = run.InstantBuildRemaining > 0f;
            bool low = !free && !run.CanAfford(BuildKind.Bridge);
            int state = free ? 2 : low ? 1 : 0;
            if (state != _fuelState)
            {
                _fuelState = state;
                _meterLabel.text = Loc.T(free ? "hud.free_builds" : low ? "hud.fuel_low" : "hud.fuel");
                Color c = free ? UITheme.AccentCyan : low ? UITheme.DangerAlt : UITheme.TextSecondary;
                _meterLabel.color = c;
                _meterIcon.color = free ? UITheme.AccentCyan : low ? UITheme.DangerAlt : FuelColor;
                _meterFrame.color = free ? UITheme.AccentCyan : low ? UITheme.DangerAlt : FuelColor;
            }

            if (low)
            {
                _meterLabel.canvasRenderer.SetAlpha(0.6f + 0.4f * Mathf.Abs(Mathf.Sin(_time * 6f)));
            }
            else if (_meterLabel.canvasRenderer.GetAlpha() < 1f)
            {
                _meterLabel.canvasRenderer.SetAlpha(1f);
            }

            if (_emptyFlash > 0f)
            {
                _emptyFlash = Mathf.Max(0f, _emptyFlash - dt * 3f);
                _meterFrame.canvasRenderer.SetAlpha(0.55f + 0.45f * _emptyFlash);
                _meterFrame.color = Color.Lerp(_meterFrame.color, UITheme.Danger, _emptyFlash);
            }
        }

        /// <summary>A build was refused for lack of fuel: the gauge flashes red and shakes.</summary>
        private void FlashEmpty()
        {
            _emptyFlash = 1f;
            _fuelState = -1;
            UITween.Shake(_meterRoot, 14f, 0.3f);
        }

        private void UpdateHull(RunSimulation run)
        {
            int max = run.MaxHull;
            if (max != _hullMaxDisplayed)
            {
                _hullMaxDisplayed = max;
                _hullDisplayed = -1;
                while (_hearts.Count < max)
                {
                    Image heart = UIFactory.Icon(_hullRoot, IconId.Heart, HeartSize, UITheme.Danger);
                    UIFactory.SetLayout(heart.rectTransform, HeartSize, HeartSize);
                    _hearts.Add(heart);
                }

                for (int i = 0; i < _hearts.Count; i++)
                {
                    _hearts[i].gameObject.SetActive(i < max);
                }
            }

            int hull = run.Hull;
            if (hull != _hullDisplayed)
            {
                bool lost = _hullDisplayed >= 0 && hull < _hullDisplayed;
                bool gained = _hullDisplayed >= 0 && hull > _hullDisplayed;
                for (int i = 0; i < max && i < _hearts.Count; i++)
                {
                    bool on = i < hull;
                    _hearts[i].color = on ? UITheme.Danger : new Color(1f, 1f, 1f, 0.18f);
                    if (lost && i == hull)
                    {
                        UITween.Punch(_hearts[i].rectTransform, 0.6f, 0.4f);
                    }
                    else if (gained && i == hull - 1)
                    {
                        UITween.Punch(_hearts[i].rectTransform, 0.45f, 0.4f);
                    }
                }

                if (lost)
                {
                    UITween.Shake(_hullRoot, 12f, 0.35f);
                }

                _hullDisplayed = hull;
            }
        }

        private void OnPause()
        {
            if (_game != null)
            {
                _game.PauseRun();
            }
        }
    }
}

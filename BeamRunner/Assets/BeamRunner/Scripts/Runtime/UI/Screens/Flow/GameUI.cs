using System;
using BeamRunner.Core;
using BeamRunner.Core.Audio;
using BeamRunner.Core.Localization;
using BeamRunner.Core.Meta;
using BeamRunner.Core.Sim;
using BeamRunner.Game;
using BeamRunner.Services;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace BeamRunner.UI
{
    /// <summary>
    /// Flow owner of the whole UI (docs/UI_SCREENS.md, "Flow owner: GameUI"). Created once by the
    /// <see cref="GameController"/> via <see cref="Create"/>: builds the <see cref="UIRoot"/> and every screen, routes
    /// controller state changes to screens, forwards run events to the HUD, shows toasts / consent / quit dialogs,
    /// follows the level palette and owns back-button behaviour. Meta screens navigate through the public methods
    /// (<see cref="ShowMissions"/>, <see cref="ShowSettings"/>, <see cref="CloseTop"/> …).
    /// <para>Startup: <see cref="Create"/> builds only the root and the splash, so the splash is in the first rendered
    /// frame. Every other screen is created empty and built behind the splash by <c>UIRoot.BuildQueue</c> (a few ms per
    /// frame, the menu first; icons are painted on a worker thread). A screen shown before its turn is built on the
    /// spot, so every navigation method works at any time.</para>
    /// <para>Layers: Splash, menu, hangar, overlays and results on Screens; the HUD on HUD; countdown, pause, revive
    /// offer and the daily-reward popup on Popups (so they draw above the HUD).</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed partial class GameUI : MonoBehaviour
    {
        private const float PausedHudAlpha = 0.5f;
        private const float CrashedHudAlpha = 0.2f;
        private const float DailyDelay = 0.9f;
        private const float DailyRetry = 0.25f;

        /// <summary>The rating prompt waits this long (s) after the menu appears, so it never greets the player instantly.</summary>
        private const float ReviewDelay = 2f;

        /// <summary>Progressive build budget while the splash is up (it has little else to do) and afterwards.</summary>
        private const float BootBuildBudgetMs = 4f;
        private const float MenuBuildBudgetMs = 2f;

        private GameController _game;
        private UIRoot _root;
        private bool _subscribed;

        private SplashScreen _splash;
        private MainMenuScreen _menu;
        private HangarScreen _hangar;
        private MissionsScreen _missions;
        private UpgradesScreen _upgrades;
        private RecordsScreen _records;
        private SettingsScreen _settings;
        private ResultsScreen _results;
        private HudScreen _hud;
        private CountdownOverlay _countdown;
        private PauseScreen _pause;
        private ReviveScreen _revive;
        private CheckpointScreen _checkpoint;
        private DailyRewardScreen _daily;

        private ZoneTheme _theme;
        private bool _consentOpen;
        private bool _dailyShownThisSession;
        private float _dailyCheckAt = -1f;
        private float _reviewCheckAt = -1f;
        private RunSimulation _countdownRun;
        private int _stateFrame = -1;
        private int _dailyClosedFrame = -1;

        /// <summary>The live flow owner (null before <see cref="Create"/>).</summary>
        public static GameUI Instance { get; private set; }

        /// <summary>The UI root (layers, stack, dialogs, toasts, fader).</summary>
        public UIRoot Root
        {
            get { return _root; }
        }

        /// <summary>The in-run HUD.</summary>
        public HudScreen Hud
        {
            get { return _hud; }
        }

        /// <summary>Builds the UI for <paramref name="game"/> (idempotent: returns the existing instance).</summary>
        public static GameUI Create(GameController game)
        {
            if (Instance != null)
            {
                return Instance;
            }

            var go = new GameObject("GameUI");
            if (game != null)
            {
                go.transform.SetParent(game.transform, false);
            }

            GameUI ui = go.AddComponent<GameUI>();
            Instance = ui;
            ui.Setup(game);
            return ui;
        }

        // ------------------------------------------------------------------ navigation API (used by the meta screens)

        /// <summary>Pushes the missions overlay.</summary>
        public void ShowMissions()
        {
            PushOverlay(_missions);
        }

        /// <summary>Pushes the upgrade shop overlay.</summary>
        public void ShowUpgrades()
        {
            PushOverlay(_upgrades);
        }

        /// <summary>Pushes the records overlay (stats / leaderboard / achievements).</summary>
        public void ShowRecords()
        {
            PushOverlay(_records);
        }

        /// <summary>Pushes the settings overlay (from the menu, or from the pause screen — back returns there).</summary>
        public void ShowSettings()
        {
            PushOverlay(_settings);
        }

        /// <summary>Opens the daily-reward popup over the menu.</summary>
        public void ShowDailyReward()
        {
            if (_daily == null || _game == null || _daily.IsVisible)
            {
                return;
            }

            if (_game.State != GameState.Menu)
            {
                return;
            }

            _dailyShownThisSession = true;
            _dailyCheckAt = -1f;
            _daily.transform.SetAsLastSibling();
            _daily.Show();

            // The popup is not on the stack, so the menu under its dimmer stayed interactable: keyboard/gamepad focus
            // (UIStack focuses the top screen's PLAY) could land behind the popup and Submit started a run through it.
            // Lock the menu until the popup closes and focus the popup's own button.
            if (_menu.IsVisible)
            {
                _menu.Group.interactable = false;
            }

            EventSystem es = EventSystem.current;
            if (es != null && _root.Stack.IsNavigating && _daily.DefaultSelection != null && _daily.DefaultSelection.activeInHierarchy)
            {
                es.SetSelectedGameObject(_daily.DefaultSelection);
            }

            ApplyBackEnabled();
        }

        /// <summary>Closes the top overlay: the daily popup if open, else pops the screen stack (never the root).</summary>
        public void CloseTop()
        {
            if (_daily != null && _daily.IsVisible)
            {
                HideDaily();
                return;
            }

            if (_root != null && _root.Stack.Count > 1)
            {
                _root.Stack.Pop();
            }
        }

        /// <summary>Shows a toast. Accent follows the icon (check = success, warning = amber, else the level accent).</summary>
        public void Toast(string text, IconId icon = IconId.Info)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            Color accent = icon == IconId.Check ? UITheme.Success : (icon == IconId.Warning ? UITheme.Warning : UITheme.Accent);
            Toasts.Show(text, icon, accent);
        }

        /// <summary>"Quit game?" confirmation (Android / desktop). No-op on iOS, where apps never quit themselves.</summary>
        public void ConfirmQuitGame()
        {
            if (Application.platform == RuntimePlatform.IPhonePlayer || Dialogs.IsOpen)
            {
                return;
            }

            Dialogs.Confirm(Loc.T("quit.title"), Loc.T("quit.body"), Loc.T("quit.confirm"), Loc.T("quit.cancel"), quit =>
            {
                if (quit)
                {
                    Application.Quit();
                }
            }, true, IconId.Warning);
        }

        // ------------------------------------------------------------------ setup

        private void Setup(GameController game)
        {
            _game = game;
            _root = UIRoot.Create(100, transform);
            if (game == null)
            {
                Debug.LogError("GameUI.Create: no GameController.");
                return;
            }

            UIFactory.OnClickSound = () => game.PlayUiSfx(SfxId.UiClick);
            UIFactory.OnHaptic = game.UiHaptic;

            _theme = game.CurrentTheme;
            UITheme.SetAccent(_theme.Primary, _theme.Secondary, false);

            RectTransform screens = _root.GetLayer(UILayer.Screens);
            RectTransform hud = _root.GetLayer(UILayer.HUD);
            RectTransform popups = _root.GetLayer(UILayer.Popups);

            // Only the splash is built now (it must be in the first rendered frame). Every other screen is created empty
            // and cheap — Bind only stores references — and built later by the build queue (QueuePrebuild), or on the spot
            // if it is shown first. Creation order = sibling (draw) order within a layer, unchanged: splash above the
            // meta screens and results.
            // Meta screens (contract: UIScreen subclasses with Bind(GameController)).
            _menu = UIScreen.CreateDeferred<MainMenuScreen>(screens);
            SafeCall(() => _menu.Bind(game));
            _hangar = UIScreen.CreateDeferred<HangarScreen>(screens);
            SafeCall(() => _hangar.Bind(game));
            _missions = UIScreen.CreateDeferred<MissionsScreen>(screens);
            SafeCall(() => _missions.Bind(game));
            _upgrades = UIScreen.CreateDeferred<UpgradesScreen>(screens);
            SafeCall(() => _upgrades.Bind(game));
            _records = UIScreen.CreateDeferred<RecordsScreen>(screens);
            SafeCall(() => _records.Bind(game));
            _settings = UIScreen.CreateDeferred<SettingsScreen>(screens);
            SafeCall(() => _settings.Bind(game));

            // Run-flow screens.
            _results = UIScreen.CreateDeferred<ResultsScreen>(screens);
            _results.Bind(game);
            _splash = UIScreen.Create<SplashScreen>(screens);
            _splash.Bind(game);
            _hud = UIScreen.CreateDeferred<HudScreen>(hud);
            _hud.Bind(game);
            _countdown = UIScreen.CreateDeferred<CountdownOverlay>(popups);
            _countdown.Bind(game);
            _pause = UIScreen.CreateDeferred<PauseScreen>(popups);
            _pause.Bind(game, ShowSettings);
            _revive = UIScreen.CreateDeferred<ReviveScreen>(popups);
            _revive.Bind(game);
            _checkpoint = UIScreen.CreateDeferred<CheckpointScreen>(popups);
            _checkpoint.Bind(game);
            _daily = UIScreen.CreateDeferred<DailyRewardScreen>(popups);
            SafeCall(() => _daily.Bind(game));

            QueuePrebuild();

            UIStack stack = _root.Stack;
            stack.BackAtRoot = OnBackAtRoot;
            stack.Changed += OnStackChanged;

            game.StateChanged += OnStateChanged;
            game.RunEventRaised += OnRunEvent;
            game.LiveMissionCompleted += OnLiveMissionCompleted;
            game.RunFinished += OnRunFinished;
            game.CountdownTicked += OnCountdownTicked;
            game.Notify += OnNotify;
            game.ConsentRequested += OnConsentRequested;
            Loc.LanguageChanged += OnLanguageChanged;
            _subscribed = true;

            EnterState(game.State, game.State);
        }

        /// <summary>
        /// Queues the progressive build of everything but the splash, soonest-needed first: the menu (shown when boot
        /// completes), the dialog window (first-launch consent, quit), the daily popup (opens right after the menu), the
        /// menu's overlays, then the run screens. Icons are painted on a worker thread meanwhile. All of it normally
        /// finishes behind the splash, which waits for the procedural audio anyway.
        /// </summary>
        private void QueuePrebuild()
        {
            UIBuildQueue queue = _root.BuildQueue;
            if (queue == null)
            {
                return;
            }

            queue.BudgetMs = BootBuildBudgetMs;
            queue.PrewarmIcons();
            queue.Enqueue(_menu);
            queue.Enqueue(Dialogs.Prewarm);
            queue.Enqueue(_daily);
            queue.Enqueue(_settings);
            queue.Enqueue(_missions);
            queue.Enqueue(_upgrades);
            queue.Enqueue(_records);
            queue.Enqueue(_hangar);
            queue.Enqueue(_hud);
            queue.Enqueue(_countdown);
            queue.Enqueue(_pause);
            queue.Enqueue(_revive);
            queue.Enqueue(_checkpoint);
            queue.Enqueue(_results);
            queue.Enqueue(() => Toasts.Prewarm(2));
        }

        private void OnDestroy()
        {
            Unsubscribe();

            if (Instance == this)
            {
                Instance = null;
                UIFactory.OnClickSound = null;
                UIFactory.OnHaptic = null;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Instance = null;
        }

        // ------------------------------------------------------------------ state routing

        private void OnStateChanged(GameState previous, GameState next)
        {
            EnterState(previous, next);
        }

        private void EnterState(GameState previous, GameState next)
        {
            _stateFrame = Time.frameCount;
            UIStack stack = _root.Stack;
            if (next != GameState.Boot && _root.BuildQueue != null)
            {
                // Whatever is still queued finishes in smaller slices once the menu is up.
                _root.BuildQueue.BudgetMs = MenuBuildBudgetMs;
            }

            if (next != GameState.Menu && _daily.IsVisible)
            {
                _daily.Hide(true);
                UnlockMenu();
            }

            switch (next)
            {
                case GameState.Boot:
                    stack.Clear(true);
                    _splash.Show(true);
                    break;

                case GameState.Menu:
                    HideRunUi();
                    HideSplash();
                    if (stack.Count != 1 || stack.Top != _menu)
                    {
                        stack.SetRoot(_menu);
                    }

                    if (!_dailyShownThisSession)
                    {
                        _dailyCheckAt = Time.unscaledTime + DailyDelay;
                    }

                    _reviewCheckAt = Time.unscaledTime + ReviewDelay;
                    break;

                case GameState.Hangar:
                    HideRunUi();
                    HideSplash();
                    stack.SetRoot(_hangar);
                    break;

                case GameState.Countdown:
                {
                    HideSplash();
                    stack.Clear();
                    RunSimulation run = _game.Run;
                    bool resume = (previous == GameState.Paused || previous == GameState.Crashed) && ReferenceEquals(run, _countdownRun);
                    _countdownRun = run;
                    if (!_hud.IsVisible)
                    {
                        _hud.Show();
                    }
                    else
                    {
                        _hud.RefreshSettings();
                        _hud.SetDimmed(false);
                    }

                    _countdown.Begin(resume ? GameController.ResumeCountdownStep : GameController.CountdownStep, resume);
                    break;
                }

                case GameState.Running:
                    HideSplash();
                    if (stack.Count > 0)
                    {
                        stack.Clear();
                    }

                    if (!_hud.IsVisible)
                    {
                        _hud.Show();
                    }
                    else if (_hud.IsDimmed)
                    {
                        _hud.SetDimmed(false);
                    }

                    break;

                case GameState.Paused:
                    _countdown.HideNow();
                    if (_hud.IsVisible)
                    {
                        _hud.SetDimmed(true, PausedHudAlpha);
                    }

                    stack.SetRoot(_pause);
                    break;

                case GameState.Crashed:
                    _countdown.HideNow();
                    if (_hud.IsVisible)
                    {
                        _hud.SetDimmed(true, CrashedHudAlpha, 0.6f);
                    }

                    if (stack.Count > 0)
                    {
                        stack.Clear();
                    }

                    break;

                case GameState.GameOver:
                    _countdown.HideNow();
                    if (_hud.IsVisible)
                    {
                        _hud.Hide();
                    }

                    stack.SetRoot(_results);
                    break;

                case GameState.Checkpoint:
                    _countdown.HideNow();
                    if (_hud.IsVisible)
                    {
                        _hud.SetDimmed(true, PausedHudAlpha);
                    }

                    stack.SetRoot(_checkpoint);
                    break;
            }

            ApplyBackEnabled();
            UpdateFps();
            CheckTheme();
        }

        private void HideRunUi()
        {
            _countdown.HideNow();
            if (_hud.IsVisible)
            {
                _hud.Hide();
            }
        }

        private void HideSplash()
        {
            if (_splash.IsVisible)
            {
                // Keep the logo on top while it cross-fades into the menu.
                _splash.transform.SetAsLastSibling();
                _splash.Hide();
            }
        }

        private void HideDaily()
        {
            if (_daily.IsVisible)
            {
                _daily.Hide();
                _dailyClosedFrame = Time.frameCount;
            }

            UnlockMenu();
            ApplyBackEnabled();
        }

        /// <summary>Undoes the menu lock taken while the daily popup is up (a hiding/hidden menu keeps its own state).</summary>
        private void UnlockMenu()
        {
            if (_menu != null && _menu.IsVisible)
            {
                _menu.Group.interactable = true;
            }
        }

        private void PushOverlay(UIScreen screen)
        {
            if (screen == null || _game == null)
            {
                return;
            }

            GameState s = _game.State;
            if (s == GameState.Boot || s == GameState.Countdown || s == GameState.Running || s == GameState.Crashed ||
                s == GameState.Checkpoint)
            {
                return;
            }

            if (_daily.IsVisible)
            {
                HideDaily();
            }

            UIStack stack = _root.Stack;
            if (stack.Top == screen)
            {
                return;
            }

            if (stack.Contains(screen))
            {
                stack.PopTo(screen);
                return;
            }

            stack.Push(screen);
        }

        private void ApplyBackEnabled()
        {
            GameState s = _game != null ? _game.State : GameState.Boot;
            bool run = s == GameState.Countdown || s == GameState.Running;
            // While the daily popup is up, GameUI routes back itself (it is not on the stack).
            _root.Stack.BackEnabled = !run && !(_daily != null && _daily.IsVisible);
        }

        private void OnStackChanged(UIScreen top)
        {
            UpdateFps();
            if (_game != null && _game.State == GameState.Paused && _hud.IsVisible)
            {
                bool pauseOnTop = top == _pause;
                _hud.SetDimmed(true, pauseOnTop ? PausedHudAlpha : 0f);
                if (pauseOnTop)
                {
                    _hud.RefreshSettings();
                }
            }
        }

        private void OnBackAtRoot()
        {
            if (_game == null)
            {
                return;
            }

            switch (_game.State)
            {
                case GameState.Menu:
                    if (_daily.IsVisible)
                    {
                        HideDaily();
                    }
                    else if (Time.frameCount > _dailyClosedFrame + 1)
                    {
                        // (The frame guard stops the back press that just closed the daily popup from also asking to quit.)
                        ConfirmQuitGame();
                    }

                    break;

                case GameState.Hangar:
                    _game.ExitHangar();
                    break;

                case GameState.Paused:
                    if (Time.frameCount > _stateFrame + 1)
                    {
                        _game.ResumeRun();
                    }

                    break;

                case GameState.GameOver:
                    // Never leave Results under a rewarded / interstitial ad (the ad overlay handles its own back key).
                    if (_game.Ads == null || !_game.Ads.IsShowing)
                    {
                        _game.ContinueFromResults(false);
                    }

                    break;
            }
        }

        // ------------------------------------------------------------------ per frame

        private void Update()
        {
            if (_game == null || _root == null)
            {
                return;
            }

            CheckTheme();
            GameState state = _game.State;
            switch (state)
            {
                case GameState.Menu:
                    UpdateLanguage();
                    if (_dailyCheckAt >= 0f && Time.unscaledTime >= _dailyCheckAt)
                    {
                        TryAutoDaily();
                    }

                    if (!TryReminderOffer())
                    {
                        TryReviewPrompt();
                    }

                    if (_daily.IsVisible && BackPressed())
                    {
                        HandleDailyBack();
                    }

                    break;

                case GameState.Hangar:
                    UpdateLanguage();
                    break;

                case GameState.Countdown:
                    // The controller only checks pause keys while running; let Escape / Start pause the 3-2-1 too.
                    if (Time.frameCount > _stateFrame && (BackPressed() || StartPressed()))
                    {
                        _game.PauseRun();
                    }

                    break;

                case GameState.Paused:
                    if (Time.frameCount > _stateFrame + 1 && _root.Stack.Top == _pause && !Dialogs.IsOpen && ResumePressed())
                    {
                        _game.ResumeRun();
                    }

                    break;

                case GameState.Crashed:
                {
                    UIStack stack = _root.Stack;
                    if (_game.ReviveOfferActive)
                    {
                        if (stack.Top != _revive)
                        {
                            stack.SetRoot(_revive);
                        }
                    }
                    else if (stack.Top == _revive)
                    {
                        stack.Clear();
                    }

                    break;
                }
            }
        }

        private void TryAutoDaily()
        {
            if (_game.State != GameState.Menu || _dailyShownThisSession)
            {
                _dailyCheckAt = -1f;
                return;
            }

            bool busy = _consentOpen || Dialogs.IsOpen || _splash.IsVisible || _splash.IsTransitioning
                || _root.Stack.Top != _menu || (_root.Fader != null && _root.Fader.IsBusy);
            if (busy)
            {
                _dailyCheckAt = Time.unscaledTime + DailyRetry;
                return;
            }

            _dailyCheckAt = -1f;
            if (_game.Profile != null && _game.Profile.DailyRewardAvailable)
            {
                ShowDailyReward();
            }
        }

        /// <summary>
        /// The one-time reminder offer (GameController.ReminderOfferPending: a daily reward was just claimed): an in-game
        /// explanation first, the OS permission prompt only after "REMIND ME". Waits until the menu is idle (daily popup
        /// closed, no dialog, no transition).
        /// </summary>
        private bool TryReminderOffer()
        {
            if (Time.frameCount % 15 != 0 || _daily.IsVisible || _consentOpen || Dialogs.IsOpen || _splash.IsVisible ||
                _root.Stack.Top != _menu || (_root.Fader != null && _root.Fader.IsBusy) || Time.frameCount - _dailyClosedFrame < 30 ||
                !_game.ReminderOfferPending)
            {
                return false;
            }

            GameController game = _game;
            game.AnswerReminderOffer(false); // marks the offer as shown right away (the real answer overrides it)
            Dialogs.Confirm(Loc.T("reminder_offer.title"), Loc.T("reminder_offer.body"), Loc.T("reminder_offer.accept"),
                Loc.T("common.no_thanks"), accept =>
                {
                    if (!accept || game == null)
                    {
                        return;
                    }

                    game.AnswerReminderOffer(true, enabled =>
                    {
                        Toast(enabled ? Loc.T("toast.reminders_on") : Loc.T("toast.reminders_off"),
                            enabled ? IconId.Check : IconId.Info);
                    });
                }, false, IconId.Calendar);
            return true;
        }

        /// <summary>
        /// The rating prompt of a prompt launch (every third, <see cref="GameController.ReviewPromptPending"/>): once the
        /// menu has been idle for a moment, after the daily reward and the reminder offer. On iOS Apple's review sheet
        /// (the game asks nothing itself: App Store Review Guideline 5.6.1); elsewhere the game's dialog — RATE NOW opens
        /// the store page, NOT NOW (also back) asks again in three launches, DON'T ASK AGAIN never does.
        /// </summary>
        private void TryReviewPrompt()
        {
            if (Time.frameCount % 15 != 0 || _reviewCheckAt < 0f || Time.unscaledTime < _reviewCheckAt ||
                _dailyCheckAt >= 0f || _daily.IsVisible || _consentOpen || Dialogs.IsOpen || _splash.IsVisible ||
                _splash.IsTransitioning || _root.Stack.Top != _menu || (_root.Fader != null && _root.Fader.IsBusy) ||
                Time.frameCount - _dailyClosedFrame < 30 || _game.ReminderOfferPending || !_game.ReviewPromptPending)
            {
                return;
            }

            GameController game = _game;
            game.BeginReviewPrompt();
            if (game.ReviewPromptStyle != ReviewPromptStyle.GameDialog)
            {
                return;
            }

            Dialogs.Choice(Loc.T("rate_prompt.title"), Loc.T("rate_prompt.body"),
                new[] { Loc.T("rate_prompt.rate"), Loc.T("common.not_now"), Loc.T("rate_prompt.never") }, choice =>
                {
                    if (game == null)
                    {
                        return;
                    }

                    ReviewAnswer answer = choice == 0 ? ReviewAnswer.Rate : choice == 2 ? ReviewAnswer.Never : ReviewAnswer.Later;
                    if (!game.AnswerReviewPrompt(answer))
                    {
                        Toast(Loc.T("toast.store_open_failed"), IconId.Warning);
                    }
                }, 1, IconId.Star);
        }

        private void HandleDailyBack()
        {
            if (Dialogs.HandleBack())
            {
                return;
            }

            bool handled = false;
            try
            {
                handled = _daily.OnBack();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }

            if (!handled)
            {
                UIFactory.PlayClickFeedback();
                HideDaily();
            }
        }

        private bool BackPressed()
        {
            if (_root.Fader != null && _root.Fader.IsBusy)
            {
                return false;
            }

            Keyboard kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame)
            {
                return true;
            }

            Gamepad pad = Gamepad.current;
            return pad != null && pad.buttonEast.wasPressedThisFrame;
        }

        private static bool StartPressed()
        {
            Gamepad pad = Gamepad.current;
            return pad != null && pad.startButton.wasPressedThisFrame;
        }

        private static bool ResumePressed()
        {
            Keyboard kb = Keyboard.current;
            if (kb != null && kb.pKey.wasPressedThisFrame)
            {
                return true;
            }

            return StartPressed();
        }

        private void CheckTheme()
        {
            ZoneTheme theme = _game.CurrentTheme;
            if (theme != null && !ReferenceEquals(theme, _theme))
            {
                _theme = theme;
                UITheme.SetAccent(theme.Primary, theme.Secondary);
            }
        }

        private void UpdateFps()
        {
            bool show = _game != null && _game.Profile != null && _game.Profile.Settings.ShowFps;
            _root.ShowFps(show);
        }

        // ------------------------------------------------------------------ controller events

        private void OnRunEvent(RunEvent e)
        {
            if (_hud != null)
            {
                _hud.HandleEvent(e);
            }

            if (e.Type == RunEventType.LevelStarted)
            {
                CheckTheme();
            }
        }

        private void OnLiveMissionCompleted(Mission mission)
        {
            if (mission == null)
            {
                return;
            }

            Toasts.Show(Loc.T("toast.mission_complete") + RunUi.Dot + MissionSystem.Describe(mission), IconId.Check, UITheme.Success, 3f);
        }

        private void OnRunFinished(RunSummary summary)
        {
            if (_game.State == GameState.GameOver && _results != null)
            {
                _results.SetSummary(summary);
            }
        }

        private void OnCountdownTicked(int value)
        {
            if (_countdown != null && (_game.State == GameState.Countdown || _game.State == GameState.Running))
            {
                _countdown.Tick(value);
            }
        }

        private void OnNotify(string message)
        {
            Toast(message);
        }

        private void OnConsentRequested(Action<bool> callback)
        {
            _consentOpen = true;
            bool crashReports = _game != null && _game.CrashReportsAvailable;
            Dialogs.Custom(Loc.T("consent.title"),
                crashReports ? Loc.T("consent.body_crash") : Loc.T("consent.body"),
                crashReports ? (Action<RectTransform>)BuildConsentBodyWithCrashReports : BuildConsentBody,
                new[] { Loc.T("consent.personalised"), Loc.T("consent.non_personalised") },
                choice =>
                {
                    _consentOpen = false;
                    if (callback != null)
                    {
                        try
                        {
                            callback(choice == 0);
                        }
                        catch (Exception e)
                        {
                            Debug.LogException(e);
                        }
                    }
                },
                -1,
                IconId.Info,
                null,
                // Equal prominence for both choices (GDPR guidance: declining must be as easy as accepting).
                new[] { ButtonStyle.Secondary, ButtonStyle.Secondary });
        }

        private static void BuildConsentBody(RectTransform body)
        {
            UIFactory.Button(body, Loc.T("consent.privacy_policy"), ButtonStyle.Ghost, OpenPrivacyPolicy, IconId.Info);
        }

        /// <summary>
        /// Consent body with the "Send crash reports" switch, pre-set by region (off = opt-in in the EEA/UK/CH or when
        /// unknown); its final position is recorded with the dialog's answer.
        /// </summary>
        private void BuildConsentBodyWithCrashReports(RectTransform body)
        {
            GameController game = _game;
            if (game != null)
            {
                bool initial = game.CrashReportsDialogDefault;
                game.SetPendingCrashReportsChoice(initial);
                UIFactory.ToggleRow(body, Loc.T("consent.crash_toggle"), initial, on => game.SetPendingCrashReportsChoice(on), IconId.Warning);
            }

            BuildConsentBody(body);
        }

        private static void OpenPrivacyPolicy()
        {
            PlatformUtil.OpenPrivacyPolicy();
        }

        private static void SafeCall(Action action)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }
    }
}

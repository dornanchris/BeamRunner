#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using BeamRunner.Core.Diagnostics;
using BeamRunner.Core.Localization;
using BeamRunner.Core.Meta;
using BeamRunner.Core.Sim;
using BeamRunner.Services;
using BeamRunner.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BeamRunner.Game
{
    /// <summary>
    /// <see cref="IGameProbe"/> implementation for the PlayMode smoke tests (Editor and development builds only).
    /// Commands call the same public methods the UI's buttons call (<see cref="GameController"/> commands, the
    /// <see cref="GameUI"/> navigation API, the UI stack's back routing); the few actions that exist only as a button
    /// (dialog choices, the daily CLAIM, the revive offer's WATCH AD, the simulated ad's CLOSE/SKIP) click that visible
    /// button through uGUI's pointer-click path, so hidden, non-interactable or blocked buttons refuse like for a
    /// player. Screens are found by what is on screen, never through GameUI internals, so lazily built screens work.
    /// </summary>
    internal sealed class GameProbe : IGameProbe
    {
        private const int MaxStoredErrors = 16;
        // Button labels in the current language (the same keys the screens use).
        private static string DialogConsentPersonalised
        {
            get { return Loc.T("consent.personalised"); }
        }

        private static string DialogConsentNonPersonalised
        {
            get { return Loc.T("consent.non_personalised"); }
        }

        private static string DailyClaimLabel
        {
            get { return Loc.T("reward.claim"); }
        }

        private static string ReviveAdLabel
        {
            get { return Loc.T("revive.watch_ad"); }
        }
        private const string AdCloseButton = "CloseButton";
        private const string AdSkipButton = "SkipButton";

        // Top-most first: a dialog's button wins over a same-named one underneath (which is blocked anyway).
        private static readonly UILayer[] ButtonLayers = { UILayer.Dialogs, UILayer.Popups, UILayer.HUD, UILayer.Screens };

        private readonly GameController _game;
        private readonly object _logLock = new object();
        private readonly List<string> _errors = new List<string>();
        private int _errorCount;
        private int _warningCount;
        private AutopilotMode _autopilot;

        private GameProbe(GameController game)
        {
            _game = game;
        }

        /// <summary>Creates the probe for <paramref name="game"/>, starts counting logs and publishes it.</summary>
        public static GameProbe Attach(GameController game)
        {
            var probe = new GameProbe(game);
            Application.logMessageReceivedThreaded += probe.OnLog;
            GameProbeRegistry.RegisterAppResetHooks(AppResetHooks.Instance);
            GameProbeRegistry.Register(probe);
            return probe;
        }

        /// <summary>Stops the probe (the controller is going away).</summary>
        public void Detach()
        {
            Application.logMessageReceivedThreaded -= OnLog;
            _autopilot = AutopilotMode.Off;
            GameProbeRegistry.Unregister(this);
        }

        /// <summary>Called by the controller with the player's sampled input, every running frame.</summary>
        public void OverrideInput(RunSimulation run, ref RunInput input)
        {
            if (_autopilot != AutopilotMode.Off && run != null)
            {
                input = RunAutopilot.Sample(run, _autopilot);
            }
        }

        // ------------------------------------------------------------------ flow & UI

        public string FlowState
        {
            get { return Game != null ? Game.State.ToString() : "NoGame"; }
        }

        public bool IsBootComplete
        {
            get { return Game != null && Game.IsBootComplete; }
        }

        public string TopScreen
        {
            get
            {
                UIRoot root = UIRoot.Instance;
                UIScreen top = root != null && root.Stack != null ? root.Stack.Top : null;
                return top != null ? top.GetType().Name : string.Empty;
            }
        }

        public IReadOnlyList<string> VisibleScreens
        {
            get
            {
                var names = new List<string>();
                UIRoot root = UIRoot.Instance;
                if (root == null)
                {
                    return names;
                }

                UIScreen[] screens = root.GetComponentsInChildren<UIScreen>(true);
                for (int i = 0; i < screens.Length; i++)
                {
                    if (screens[i] != null && screens[i].IsVisible)
                    {
                        names.Add(screens[i].GetType().Name);
                    }
                }

                return names;
            }
        }

        public bool DialogOpen
        {
            get { return Dialogs.IsOpen; }
        }

        public IReadOnlyList<string> DialogOptions
        {
            get
            {
                var labels = new List<string>();
                UIRoot root = UIRoot.Instance;
                if (root == null || !Dialogs.IsOpen)
                {
                    return labels;
                }

                NeonButton[] buttons = root.GetLayer(UILayer.Dialogs).GetComponentsInChildren<NeonButton>(false);
                for (int i = 0; i < buttons.Length; i++)
                {
                    string label = LabelOf(buttons[i]);
                    if (label.Length > 0)
                    {
                        labels.Add(label);
                    }
                }

                return labels;
            }
        }

        public IReadOnlyList<string> PressableButtons
        {
            get
            {
                var labels = new List<string>();
                UIRoot root = UIRoot.Instance;
                if (root == null)
                {
                    return labels;
                }

                for (int l = 0; l < ButtonLayers.Length; l++)
                {
                    NeonButton[] buttons = root.GetLayer(ButtonLayers[l]).GetComponentsInChildren<NeonButton>(false);
                    for (int i = 0; i < buttons.Length; i++)
                    {
                        string label = LabelOf(buttons[i]);
                        if (label.Length > 0 && buttons[i].IsInteractable())
                        {
                            labels.Add(label);
                        }
                    }
                }

                return labels;
            }
        }

        public bool UiTransitioning
        {
            get
            {
                UIRoot root = UIRoot.Instance;
                if (root == null)
                {
                    return false;
                }

                if (root.Fader != null && root.Fader.IsBusy)
                {
                    return true;
                }

                GameUI ui = GameUI.Instance;
                if (ui != null && ui.LanguageRebuildPending)
                {
                    return true;
                }

                UIScreen[] screens = root.GetComponentsInChildren<UIScreen>(false);
                for (int i = 0; i < screens.Length; i++)
                {
                    if (screens[i] != null && screens[i].IsTransitioning)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        // ------------------------------------------------------------------ profile

        public long Gems
        {
            get { return Profile != null ? Profile.Gems : 0; }
        }

        public int RunsPlayed
        {
            get { return Profile != null ? Profile.Profile.Stats.Runs : 0; }
        }

        public long BestScore
        {
            get { return Profile != null ? Profile.Profile.Stats.BestScore : 0; }
        }

        public bool TutorialDone
        {
            get { return Profile != null && Profile.Profile.Flags.TutorialDone; }
        }

        public bool ConsentAsked
        {
            get { return Profile != null && Profile.Profile.Flags.ConsentAsked; }
        }

        public bool DailyRewardAvailable
        {
            get { return Profile != null && Profile.DailyRewardAvailable; }
        }

        public string SaveFilePath
        {
            get
            {
                // Never create the services from here (ServiceHub.Save would): only report what the game set up.
                if (!ServiceHub.IsCreated)
                {
                    return string.Empty;
                }

                var file = ServiceHub.Save as FileSaveStorage;
                return file != null ? file.FilePath : string.Empty;
            }
        }

        // ------------------------------------------------------------------ run

        public bool HasRun
        {
            get { return Run != null; }
        }

        public bool RunIsTutorial
        {
            get { return Run != null && Run.Config.Tutorial; }
        }

        public double RunDistance
        {
            get { return Run != null ? Run.Distance : 0.0; }
        }

        public long RunScore
        {
            get { return Run != null ? Run.Score : 0; }
        }

        public int RunLevel
        {
            get { return Run != null ? Run.Level : 0; }
        }

        public bool RunCrashed
        {
            get { return Run != null && Run.IsCrashed; }
        }

        public int RevivesUsed
        {
            get { return Run != null ? Run.RevivesUsed : 0; }
        }

        public bool ReviveOfferActive
        {
            get { return Game != null && Game.ReviveOfferActive; }
        }

        public bool AdShowing
        {
            get { return Game != null && Game.Ads != null && Game.Ads.IsShowing; }
        }

        public float TimeScale
        {
            get { return Time.timeScale; }
        }

        public AutopilotMode Autopilot
        {
            get { return _autopilot; }
            set { _autopilot = value; }
        }

        // ------------------------------------------------------------------ logs

        public int ErrorCount
        {
            get
            {
                lock (_logLock)
                {
                    return _errorCount;
                }
            }
        }

        public int WarningCount
        {
            get
            {
                lock (_logLock)
                {
                    return _warningCount;
                }
            }
        }

        public IReadOnlyList<string> Errors
        {
            get
            {
                lock (_logLock)
                {
                    return _errors.ToArray();
                }
            }
        }

        public string Describe()
        {
            var sb = new StringBuilder(512);
            sb.Append("[probe] state=").Append(FlowState);
            sb.Append(" boot=").Append(IsBootComplete ? "done" : "running");
            sb.Append(" lang=").Append(Language); // l10n-ok: diagnostics
            sb.Append(" top=").Append(Or(TopScreen, "-"));
            sb.Append(" visible=[").Append(string.Join(",", VisibleScreens)).Append(']');
            if (DialogOpen)
            {
                sb.Append(" dialog=[").Append(string.Join("|", DialogOptions)).Append(']');
            }

            sb.Append(" buttons=[").Append(string.Join("|", PressableButtons)).Append(']');
            if (HasRun)
            {
                sb.Append(" run={tutorial=").Append(RunIsTutorial);
                sb.Append(" dist=").Append(RunDistance.ToString("F0", CultureInfo.InvariantCulture));
                sb.Append(" score=").Append(RunScore);
                sb.Append(" level=").Append(RunLevel);
                sb.Append(" crashed=").Append(RunCrashed);
                sb.Append(" revives=").Append(RevivesUsed).Append('}');
            }

            sb.Append(" offer=").Append(ReviveOfferActive);
            sb.Append(" ad=").Append(AdShowing);
            sb.Append(" timeScale=").Append(TimeScale.ToString("0.##", CultureInfo.InvariantCulture));
            sb.Append(" autopilot=").Append(_autopilot);
            sb.Append(" dailyRun=").Append(IsDailyRun).Append(" ghost=").Append(GhostVisible);
            sb.Append(" profile={gems=").Append(Gems);
            sb.Append(" runs=").Append(RunsPlayed);
            sb.Append(" best=").Append(BestScore);
            sb.Append(" tutorialDone=").Append(TutorialDone);
            sb.Append(" consent=").Append(ConsentAsked);
            sb.Append(" daily=").Append(DailyRewardAvailable).Append('}');
            sb.Append(" save=").Append(Or(SaveFilePath, "-"));
            IReadOnlyList<string> errors = Errors;
            sb.Append(" errors=").Append(ErrorCount).Append(" warnings=").Append(WarningCount);
            if (errors.Count > 0)
            {
                sb.Append(" firstError=\"").Append(errors[0]).Append('"');
            }

            return sb.ToString();
        }

        // ------------------------------------------------------------------ commands

        public bool PressButton(string label)
        {
            for (int l = 0; l < ButtonLayers.Length; l++)
            {
                if (PressIn(ButtonLayers[l], label))
                {
                    return true;
                }
            }

            return false;
        }

        public bool AnswerConsent(bool personalised)
        {
            return Dialogs.IsOpen &&
                   PressIn(UILayer.Dialogs, personalised ? DialogConsentPersonalised : DialogConsentNonPersonalised);
        }

        public bool ClaimDailyReward()
        {
            long before = Gems;
            return PressIn(UILayer.Popups, DailyClaimLabel) && Gems > before;
        }

        public bool OpenDailyReward()
        {
            GameUI ui = GameUI.Instance;
            if (ui == null)
            {
                return false;
            }

            ui.ShowDailyReward();
            return IsScreenVisible(nameof(DailyRewardScreen));
        }

        public bool StartRun()
        {
            if (Game == null)
            {
                return false;
            }

            Game.StartRun();
            return Game.State == GameState.Countdown;
        }

        public bool PauseRun()
        {
            if (Game == null)
            {
                return false;
            }

            Game.PauseRun();
            return Game.State == GameState.Paused;
        }

        public bool ResumeRun()
        {
            if (Game == null || Game.State != GameState.Paused)
            {
                return false;
            }

            Game.ResumeRun();
            return Game.State == GameState.Countdown;
        }

        public bool OpenSettings()
        {
            return Navigate(ui => ui.ShowSettings(), nameof(SettingsScreen));
        }

        public bool OpenMissions()
        {
            return Navigate(ui => ui.ShowMissions(), nameof(MissionsScreen));
        }

        public bool OpenRecords()
        {
            return Navigate(ui => ui.ShowRecords(), nameof(RecordsScreen));
        }

        public bool EnterHangar()
        {
            if (Game == null)
            {
                return false;
            }

            Game.EnterHangar();
            return Game.State == GameState.Hangar;
        }

        public bool ExitHangar()
        {
            if (Game == null)
            {
                return false;
            }

            Game.ExitHangar();
            return Game.State == GameState.Menu;
        }

        public bool CloseTop()
        {
            GameUI ui = GameUI.Instance;
            if (ui == null)
            {
                return false;
            }

            string topBefore = TopScreen;
            bool dailyBefore = IsScreenVisible(nameof(DailyRewardScreen));
            ui.CloseTop();
            return (dailyBefore && !IsScreenVisible(nameof(DailyRewardScreen))) || TopScreen != topBefore;
        }

        public bool Back()
        {
            // What UIStack does for Escape / Android back / gamepad B.
            UIRoot root = UIRoot.Instance;
            if (root == null || root.Stack == null || !root.Stack.BackEnabled || (root.Fader != null && root.Fader.IsBusy))
            {
                return false;
            }

            return root.Stack.HandleBack();
        }

        public bool ReviveWithAd()
        {
            return Game != null && Game.ReviveOfferActive && PressIn(UILayer.Popups, ReviveAdLabel);
        }

        public bool ReviveWithGems()
        {
            return Game != null && Game.ReviveWithGems();
        }

        public bool DeclineRevive()
        {
            if (Game == null || Game.State != GameState.Crashed)
            {
                return false;
            }

            Game.DeclineRevive();
            return Game.State == GameState.GameOver;
        }

        public bool FinishSimulatedAd(bool watchedToEnd)
        {
            SimulatedAdOverlay overlay = UnityEngine.Object.FindAnyObjectByType<SimulatedAdOverlay>();
            if (overlay == null)
            {
                return false;
            }

            string wanted = watchedToEnd ? AdCloseButton : AdSkipButton;
            Button[] buttons = overlay.GetComponentsInChildren<Button>(false);
            for (int i = 0; i < buttons.Length; i++)
            {
                if (buttons[i].name == wanted)
                {
                    return Click(buttons[i]);
                }
            }

            return false;
        }

        public bool ContinueFromResults(bool retry)
        {
            if (Game == null || Game.State != GameState.GameOver)
            {
                return false;
            }

            Game.ContinueFromResults(retry);
            // Accepted when the results were left, or an interstitial is now on its way first.
            return Game.State != GameState.GameOver || AdShowing;
        }

        public void FlushSave()
        {
            if (Game != null && Game.Store != null)
            {
                Game.Store.SaveNow(true);
            }
        }

        // ------------------------------------------------------------------ daily run & ghost

        public bool DailyRunAvailable
        {
            get { return Game != null && Game.DailyRunAvailable; }
        }

        public bool StartDailyRun()
        {
            if (Game == null)
            {
                return false;
            }

            Game.StartDailyRun();
            return Game.State == GameState.Countdown && Game.IsDailyRun;
        }

        public bool IsDailyRun
        {
            get { return Game != null && Game.IsDailyRun; }
        }

        public bool GhostVisible
        {
            get { return Game != null && Game.GhostVisible; }
        }

        public long DailyBestScore
        {
            get { return Profile != null ? Profile.DailyRunBestScoreToday : 0; }
        }

        public int DailyStreak
        {
            get { return Profile != null ? Profile.DailyRunStreak : 0; }
        }

        public bool ResultsShowDaily
        {
            get
            {
                UIRoot root = UIRoot.Instance;
                if (root == null)
                {
                    return false;
                }

                // Found by what is on screen (the results may have been built lazily).
                ResultsScreen[] screens = root.GetComponentsInChildren<ResultsScreen>(false);
                for (int i = 0; i < screens.Length; i++)
                {
                    if (screens[i] != null && screens[i].DailyPanelShown)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        // ------------------------------------------------------------------ language

        public string Language
        {
            get { return Loc.Language.Code; }
        }

        public bool SetLanguage(string code)
        {
            if (Game == null || (Game.State != GameState.Menu && Game.State != GameState.Hangar))
            {
                return false;
            }

            LanguageInfo language = null;
            if (!string.IsNullOrEmpty(code))
            {
                language = Languages.Find(code);
                if (language == null || (language.IsPseudo && !LocalizationService.PseudoAvailable))
                {
                    return false;
                }
            }

            LocalizationService.SetChoice(language);
            return true;
        }

        // ------------------------------------------------------------------ helpers

        private GameController Game
        {
            get { return _game != null ? _game : null; }
        }

        private ProfileService Profile
        {
            get { return Game != null ? Game.Profile : null; }
        }

        private RunSimulation Run
        {
            get { return Game != null ? Game.Run : null; }
        }

        private static bool IsScreenVisible(string typeName)
        {
            UIRoot root = UIRoot.Instance;
            if (root == null)
            {
                return false;
            }

            UIScreen[] screens = root.GetComponentsInChildren<UIScreen>(false);
            for (int i = 0; i < screens.Length; i++)
            {
                if (screens[i] != null && screens[i].IsVisible && screens[i].GetType().Name == typeName)
                {
                    return true;
                }
            }

            return false;
        }

        private bool Navigate(Action<GameUI> open, string expectedTop)
        {
            GameUI ui = GameUI.Instance;
            if (ui == null)
            {
                return false;
            }

            open(ui);
            return TopScreen == expectedTop;
        }

        private static bool PressIn(UILayer layer, string label)
        {
            UIRoot root = UIRoot.Instance;
            if (root == null || string.IsNullOrEmpty(label))
            {
                return false;
            }

            NeonButton[] buttons = root.GetLayer(layer).GetComponentsInChildren<NeonButton>(false);
            for (int i = 0; i < buttons.Length; i++)
            {
                if (string.Equals(LabelOf(buttons[i]), label.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return Click(buttons[i]);
                }
            }

            return false;
        }

        /// <summary>A tap: uGUI's pointer-click handler, which refuses inactive or non-interactable buttons.</summary>
        private static bool Click(Selectable button)
        {
            if (button == null || !button.IsActive() || !button.IsInteractable())
            {
                return false;
            }

            var data = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
            return ExecuteEvents.Execute(button.gameObject, data, ExecuteEvents.pointerClickHandler);
        }

        private static string LabelOf(NeonButton button)
        {
            Text label = button != null ? button.Label : null;
            if (label == null || !label.gameObject.activeSelf || string.IsNullOrEmpty(label.text))
            {
                return string.Empty;
            }

            return label.text.Trim();
        }

        private static string Or(string value, string fallback)
        {
            return string.IsNullOrEmpty(value) ? fallback : value;
        }

        private void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Log)
            {
                return;
            }

            lock (_logLock)
            {
                if (type == LogType.Warning)
                {
                    _warningCount++;
                    return;
                }

                _errorCount++;
                if (_errors.Count < MaxStoredErrors)
                {
                    _errors.Add(type + ": " + condition + FirstLine(stackTrace));
                }
            }
        }

        private static string FirstLine(string stackTrace)
        {
            if (string.IsNullOrEmpty(stackTrace))
            {
                return string.Empty;
            }

            int end = stackTrace.IndexOf('\n');
            return " @ " + (end >= 0 ? stackTrace.Substring(0, end) : stackTrace).Trim();
        }
    }
}
#endif

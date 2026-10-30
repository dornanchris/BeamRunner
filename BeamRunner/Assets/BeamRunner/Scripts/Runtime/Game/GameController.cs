using System;
using System.Collections;
using BeamRunner.Core;
using BeamRunner.Core.Audio;
using BeamRunner.Core.Field;
using BeamRunner.Core.Meta;
using BeamRunner.Core.Sim;
using BeamRunner.Services;
using UnityEngine;

namespace BeamRunner.Game
{
    /// <summary>
    /// The game's conductor: owns the app state machine, the player's run and the menu "attract" autopilot,
    /// and exposes every command the UI needs. Presentation (rendering/audio/haptics) lives in
    /// <c>GameController.Presentation.cs</c>, services wiring in <c>GameController.Services.cs</c>, and UI
    /// creation in <c>GameController.UI.cs</c>; the UI observes this class through its events and read-only
    /// state and never implements game rules itself.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed partial class GameController : MonoBehaviour
    {
        // ------------------------------------------------------------------ tuning

        public const float CountdownStep = 0.6f;
        public const float ResumeCountdownStep = 0.45f;
        public const float CrashSlowMotionScale = 0.3f;
        public const float CrashSlowMotionSeconds = 0.9f;
        public const float CrashToOfferSeconds = 1.25f;
        public const float ReviveOfferSeconds = 6f;
        public const float AttractTimeScale = 0.85f;
        public const float HangarAttractScale = 0.4f;
        public const float BootMaxWaitSeconds = 4f;
        private const double AttractResetDistance = 1500.0;
        private const float LiveMissionCheckInterval = 0.5f;
        private const float MaxOfferTimerStep = 0.1f;

        // ------------------------------------------------------------------ state

        private RunSimulation _attract;
        private float _countdownTimer;
        private float _countdownStep;
        private float _crashTimer;
        private float _liveMissionTimer;
        private bool[] _missionAnnounced = new bool[MissionSystem.ActiveCount];
        private bool _pendingInterstitial;
        private bool _adPausedOffer;
        private bool _leavingResults;
        private int _adFlowGeneration;
        private int _eventFailureLogs;

        public static GameController Instance { get; private set; }

        public GameState State { get; private set; } = GameState.Boot;

        /// <summary>Raised on every state change: (previous, next).</summary>
        public event Action<GameState, GameState> StateChanged;

        /// <summary>Every simulation event of the player's run (not the attract run), after presentation.</summary>
        public event Action<RunEvent> RunEventRaised;

        /// <summary>A mission completed during the current run (announced live; rewarded at game over).</summary>
        public event Action<Mission> LiveMissionCompleted;

        /// <summary>The run ended and its results were applied.</summary>
        public event Action<RunSummary> RunFinished;

        /// <summary>Countdown value changed: 3, 2, 1, then 0 for "GO".</summary>
        public event Action<int> CountdownTicked;

        /// <summary>A short message the UI should toast (purchases restored, errors...).</summary>
        public event Action<string> Notify;

        /// <summary>The UI must show the ads-consent dialog and call back with the player's choice.</summary>
        public event Action<Action<bool>> ConsentRequested;

        public ProfileStore Store { get; private set; }

        public ProfileService Profile { get; private set; }

        public InputRouter Input { get; private set; }

        public MonetizationConfig Config { get; private set; }

        public IAdsService Ads { get; private set; }

        public IIapService Iap { get; private set; }

        /// <summary>The player's run (null outside runs).</summary>
        public RunSimulation Run { get; private set; }

        /// <summary>The simulation being rendered: the player's run, or the menu autopilot.</summary>
        public RunSimulation DisplayedSim
        {
            get { return Run ?? _attract; }
        }

        /// <summary>Results of the last finished run.</summary>
        public RunSummary LastSummary { get; private set; }

        public float BootProgress { get; private set; }

        public bool IsBootComplete { get; private set; }

        /// <summary>Current countdown number (3..1, 0 = GO) while in <see cref="GameState.Countdown"/>.</summary>
        public int CountdownValue { get; private set; }

        /// <summary>True while the crash "continue?" offer is on screen.</summary>
        public bool ReviveOfferActive { get; private set; }

        /// <summary>Seconds left on the continue offer.</summary>
        public float ReviveOfferRemaining { get; private set; }

        public int ReviveGemCost
        {
            get { return ProfileService.ReviveGemCost; }
        }

        public bool CanReviveWithAd
        {
            get { return Ads != null && Ads.RewardedReady; }
        }

        public bool CanReviveWithGems
        {
            get { return Profile != null && Profile.Gems >= ReviveGemCost; }
        }

        /// <summary>Ship shown in the hangar (may differ from the selected ship while browsing).</summary>
        public string PreviewShipId { get; private set; }

        /// <summary>
        /// Where the ship is on screen (pixels, origin bottom-left) — lets the HUD spawn popups next to it.
        /// Updated every frame by the presentation layer; defaults to the lower-centre of the screen.
        /// </summary>
        public Vector2 ShipScreenPoint { get; private set; } = new Vector2(Screen.width * 0.5f, Screen.height * 0.3f);

        /// <summary>The level palette currently shown (tracks the displayed simulation, cross-fades aside).</summary>
        public ZoneTheme CurrentTheme
        {
            get { return ZoneThemes.ForLevel(DisplayedSim != null ? DisplayedSim.Level : 1); }
        }

        /// <summary>Plays a UI sound (the UI kit's click hook and screens call this).</summary>
        public void PlayUiSfx(SfxId id)
        {
            PresentUiSound(id);
        }

        /// <summary>Light haptic tick for UI presses (respects the haptics setting).</summary>
        public void UiHaptic()
        {
            Haptics.Selection();
        }

        // ------------------------------------------------------------------ lifecycle

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            Time.timeScale = 1f;
            ProbeAttach();
            StartCoroutine(Boot());
        }

        private void OnDestroy()
        {
            ProbeDetach();
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private IEnumerator Boot()
        {
            // Every step is guarded: an exception escaping a coroutine silently ends it, which would leave the
            // game on the splash screen forever (Boot state, IsBootComplete false). A failed step is logged and
            // boot carries on, so the player always reaches the menu with whatever did initialise.
            // The first frame only gets what the splash needs, plus the start of the audio worker (the longest boot task,
            // which decides when the menu can appear). The world and the rest of the UI are built behind the splash: the
            // world in the next frame, the other screens progressively (GameUI / UIRoot.BuildQueue).
            int bootFrame = Time.frameCount;
            SetBootProgress(0.02f);
            Input = new InputRouter();
            RunBootStep("platform/services/profile", BootCore);
            if (Profile == null)
            {
                // Storage or profile loading blew up: play on a fresh profile that simply isn't persisted.
                Store = new ProfileStore(null);
                Profile = Store.Load();
            }

            PreviewShipId = Profile.Profile.SelectedShip;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // Lets testers start at any level from the menu's level picker; release builds unlock levels by reaching them.
            Profile.AllStartLevelsUnlocked = true;
#endif
            RunBootStep("audio", () => AudioBuild());
            RunBootStep("UI", () => CreateUI());
            SetBootProgress(0.1f);

            // Let the splash reach the screen first. Started from Awake during scene load, this coroutine resumes from its
            // first `yield return null` in the first frame's Update phase — before that frame renders — so wait one more.
            do
            {
                yield return null;
            }
            while (Time.frameCount <= bootFrame + 1);

            RunBootStep("presentation", () => BuildPresentation());
            RunBootStep("settings", ApplySettings);
            SetBootProgress(0.25f);
            yield return null;

            RunBootStep("services", () => InitializeServices());
            RunBootStep("attract", StartAttract);
            SetBootProgress(0.35f);

            // Give procedural audio a moment to render (menu music first); never block forever.
            float waited = 0f;
            while (waited < BootMaxWaitSeconds && !IsPresentationReadySafe())
            {
                waited += Time.unscaledDeltaTime;
                SetBootProgress(Mathf.Lerp(0.35f, 0.95f, PresentationLoadProgress()));
                yield return null;
            }

            SetBootProgress(1f);
            IsBootComplete = true;
            SetState(GameState.Menu);
        }

        private void BootCore()
        {
            PlatformUtil.ApplyFrameRate(60);

            // The validator must reject any document JsonUtility cannot turn into a profile, so FileSaveStorage
            // falls back to its .bak copy instead of later rotating the unreadable file over that good backup.
            BalanceLoader.Load(); // before the profile: upgrade maxima clamp the saved levels
            ServiceHub.Create(this, ProfileStore.IsUsableSave);
            Store = new ProfileStore(ServiceHub.Save);
            Profile = Store.Load();
            AutoDetectGraphicsQuality();
        }

        private static void RunBootStep(string name, Action step)
        {
            try
            {
                step();
            }
            catch (Exception e)
            {
                FieldLog.Exception("Boot step '" + name + "'", e);
            }
        }

        private bool IsPresentationReadySafe()
        {
            try
            {
                return PresentationReady();
            }
            catch (Exception e)
            {
                FieldLog.Exception("Presentation readiness query", e);
                return true;
            }
        }

        private void SetBootProgress(float value)
        {
            BootProgress = Mathf.Clamp01(value);
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            float unscaled = Time.unscaledDeltaTime;
            Store?.Tick(unscaled);

            switch (State)
            {
                case GameState.Boot:
                case GameState.Menu:
                    TickAttract(dt);
                    break;
                case GameState.Hangar:
                    // The hangar keeps the attract run going, slowed down with its obstacles hidden, so the field
                    // (scroll, theme) stays continuous when the player returns to the menu.
                    TickAttract(dt * HangarAttractScale);
                    break;
                case GameState.Countdown:
                    TickCountdown(unscaled);
                    break;
                case GameState.Running:
                    TickRunning(dt, unscaled);
                    break;
                case GameState.Crashed:
                    TickCrashed(unscaled);
                    break;
                case GameState.Paused:
                case GameState.GameOver:
                    break;
            }

            TickPresentation(dt, unscaled);
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused)
            {
                if (State == GameState.Running || State == GameState.Countdown)
                {
                    PauseRun();
                }

                Store?.SaveNow(true);
            }
        }

        private void OnApplicationFocus(bool focused)
        {
            // Losing focus mid-run (or during the 3-2-1) pauses. On Android the notification shade, quick settings
            // and system dialogs only take window focus: Unity sends OnApplicationFocus(false) without
            // OnApplicationPause(true) and keeps running, so without this the ship flew on and crashed. No ads, IAP
            // sheets or keyboards appear during a run, so nothing of ours triggers it. (Editor excluded: clicking
            // another window while testing would pause.)
            if (!focused && !Application.isEditor && (State == GameState.Running || State == GameState.Countdown))
            {
                PauseRun();
            }
        }

        private void OnApplicationQuit()
        {
            Store?.SaveNow(true);
        }

        // ------------------------------------------------------------------ state machine

        private void SetState(GameState next)
        {
            if (next == State)
            {
                return;
            }

            GameState previous = State;
            State = next;

            // The state has already changed: a presentation or UI bug must not abort the caller half-way (e.g.
            // FinishRun before RunFinished, or Boot before the consent request) nor keep the UI from hearing it.
            try
            {
                OnStatePresentation(previous, next);
            }
            catch (Exception e)
            {
                FieldLog.Exception("State presentation (" + previous + " -> " + next + ")", e);
            }

            try
            {
                StateChanged?.Invoke(previous, next);
            }
            catch (Exception e)
            {
                FieldLog.Exception("StateChanged handler (" + previous + " -> " + next + ")", e);
            }

            if (next == GameState.Menu)
            {
                RequestConsentIfPending();
            }
        }

        // ------------------------------------------------------------------ attract mode (menu background)

        private void StartAttract()
        {
            // A random level each time, so the menu background shows every kind of chunk; the bot gets a roomy fuel tank
            // and never crashes (the background should never end in an explosion), and checkpoints pass on their own.
            int level = UnityEngine.Random.Range(1, Levels.NamedLevels + 1);
            _attract = new RunSimulation(new RunConfig
            {
                Seed = UnityEngine.Random.Range(1, int.MaxValue),
                MaxRevives = 0,
                StartLevel = level,
                Ship = Core.Balance.ShipStats.FromLevels(new[] { 10, 6, 10, 4, 0 }),
                Invincible = true,
                AutoCheckpoint = true,
            });
            OnSimulationReplaced();
        }

        private void TickAttract(float dt)
        {
            if (_attract == null || Run != null)
            {
                return;
            }

            if (_attract.IsCrashed || _attract.Distance > AttractResetDistance)
            {
                StartAttract();
            }

            _attract.Step(dt * AttractTimeScale, Core.Diagnostics.RunAutopilot.FollowLine(_attract));
            try
            {
                for (int i = 0; i < _attract.Events.Count; i++)
                {
                    PresentAttractEvent(_attract.Events[i]);
                }
            }
            finally
            {
                // Always drain, or a throwing view would make the event list grow every frame.
                _attract.ClearEvents();
            }
        }

        // ------------------------------------------------------------------ runs

        /// <summary>Starts a new run from the menu, hangar or results screen.</summary>
        public void StartRun()
        {
            // Only from the menu, hangar or results. A second tap on PLAY/RETRY that lands after the state
            // changed would otherwise silently throw the fresh run away and start another.
            if (!IsBootComplete ||
                (State != GameState.Menu && State != GameState.Hangar && State != GameState.GameOver))
            {
                return;
            }

            if (State != GameState.GameOver)
            {
                _dailyMode = false; // PLAY is a normal run; RETRY on the results keeps a Daily Run a Daily Run
            }

            BeginRun();
        }

        /// <summary>Abandons the current run (counting its result) and starts another.</summary>
        public void RestartRun()
        {
            // Only from inside a run or its results. A double tap on the pause menu's RESTART would otherwise
            // arrive during the new run's countdown and FinishRun that one too: an extra zero-length run in the
            // stats, missions ("play N runs") and interstitial pacing.
            if (!IsBootComplete || (State != GameState.Paused && State != GameState.Running &&
                                    State != GameState.Crashed && State != GameState.GameOver))
            {
                return;
            }

            if (Run != null && State != GameState.GameOver)
            {
                FinishRun(false);
            }

            BeginRun();
        }

        private void BeginRun()
        {
            Time.timeScale = 1f;
            ReviveOfferActive = false;
            ResetAdFlowState();
            LastSummary = null;
            _attract = null;
            // Wall-clock seed: Environment.TickCount reads the boot-time clock, an Apple required-reason API.
            int seed = unchecked((int)DateTime.UtcNow.Ticks * 31 + Profile.Profile.Stats.Runs * 7919);
            RunConfig config = CreateRunConfigForMode(seed); // Daily Run: today's seed and rules (GameController.DailyRun.cs)
            if (FootageMode.Enabled)
            {
                config.Invincible = true; // footage runs never end in a crash
                config.AutoCheckpoint = true;
                config.Tutorial = false;
                config.Adaptive = false;
            }

            Run = new RunSimulation(config);
            RecordGhostFrame();
            Array.Clear(_missionAnnounced, 0, _missionAnnounced.Length);
            _liveMissionTimer = LiveMissionCheckInterval;
            Input.Mode = Profile.Settings.ControlMode;
            Input.ResetGestures();
            OnSimulationReplaced();
            _countdownIsFresh = true;
            try
            {
                BeginCountdown(CountdownStep);
            }
            finally
            {
                _countdownIsFresh = false;
            }
        }

        /// <summary>
        /// True while <see cref="BeginRun"/> enters the countdown of a new run: per-run presentation and audio state reset
        /// then. RESTART from Pause is a new run although the countdown comes from <see cref="GameState.Paused"/>; resume and
        /// revive countdowns are not.
        /// </summary>
        private bool _countdownIsFresh;

        private void BeginCountdown(float step)
        {
            _countdownStep = step;
            _countdownTimer = step;
            CountdownValue = 3;
            SetState(GameState.Countdown);
            RaiseCountdownTicked(CountdownValue);
            PresentCountdown(CountdownValue);
        }

        private void TickCountdown(float unscaled)
        {
            _countdownTimer -= unscaled;
            if (_countdownTimer > 0f)
            {
                return;
            }

            _countdownTimer += _countdownStep;
            CountdownValue--;
            RaiseCountdownTicked(CountdownValue);
            PresentCountdown(CountdownValue);
            if (CountdownValue <= 0)
            {
                Time.timeScale = 1f;
                Input.ResetGestures();
                SetState(GameState.Running);
            }
        }

        private void TickRunning(float dt, float unscaled)
        {
            if (Run == null)
            {
                return;
            }

            if (Input.PausePressed())
            {
                PauseRun();
                return;
            }

            RunInput input = FootageMode.Enabled ? Core.Diagnostics.RunAutopilot.FollowLine(Run) : Input.Sample();
            ProbeOverrideInput(ref input);
            Run.Step(dt, input);
            RecordGhostFrame();
            DispatchRunEvents();

            _liveMissionTimer -= unscaled;
            if (_liveMissionTimer <= 0f && Run != null)
            {
                _liveMissionTimer = LiveMissionCheckInterval;
                CheckLiveMissions();
            }
        }

        private void DispatchRunEvents()
        {
            RunSimulation run = Run;
            if (run == null)
            {
                return;
            }

            try
            {
                for (int i = 0; i < run.Events.Count; i++)
                {
                    RunEvent e = run.Events[i];

                    // Feedback (VFX/audio/haptics/HUD) is isolated per event: if it threw, the loop would stop
                    // before a later Crashed event and ClearEvents would never run — the sim would sit crashed
                    // (Step is a no-op then) while the state machine stayed in Running forever.
                    try
                    {
                        PresentRunEvent(e);
                        RunEventRaised?.Invoke(e);
                    }
                    catch (Exception ex)
                    {
                        ReportEventFailure(e.Type, ex);
                    }

                    if (e.Type == RunEventType.Crashed)
                    {
                        OnCrashed();
                    }
                    else if (e.Type == RunEventType.CheckpointReached && !run.Config.AutoCheckpoint)
                    {
                        OnCheckpointReached(e.Count);
                    }
                }
            }
            finally
            {
                run.ClearEvents();
            }
        }

        private void ReportEventFailure(RunEventType type, Exception e)
        {
            // Keep the log readable if a feedback bug fires every frame.
            if (_eventFailureLogs < 10)
            {
                _eventFailureLogs++;
                FieldLog.Exception("Run event feedback (" + type + ")", e);
            }
        }

        private void CheckLiveMissions()
        {
            var active = Profile.Profile.Missions.Active;
            RunResult live = null;
            for (int i = 0; i < active.Count && i < _missionAnnounced.Length; i++)
            {
                if (_missionAnnounced[i])
                {
                    continue;
                }

                live = live ?? Run.BuildResult();
                if (MissionSystem.WouldComplete(active[i], live))
                {
                    _missionAnnounced[i] = true;
                    PresentMissionComplete(active[i]);
                    LiveMissionCompleted?.Invoke(active[i]);
                }
            }
        }

        /// <summary>HUD build button (or a test): builds on the next tile like a tap or swipe.</summary>
        public void RequestBuild(BuildKind kind)
        {
            Input?.RequestBuild(kind);
        }

        public void PauseRun()
        {
            if (State != GameState.Running && State != GameState.Countdown)
            {
                return;
            }

            Time.timeScale = 0f;
            SetState(GameState.Paused);
        }

        /// <summary>Leaves the pause menu with a short 3-2-1.</summary>
        public void ResumeRun()
        {
            if (State != GameState.Paused)
            {
                return;
            }

            Time.timeScale = 1f;
            Input.Mode = Profile.Settings.ControlMode;
            Input.ResetGestures();
            BeginCountdown(ResumeCountdownStep);
        }

        /// <summary>Back to the main menu. An unfinished run still counts toward stats and missions.</summary>
        public void QuitToMenu()
        {
            if (!IsBootComplete)
            {
                return;
            }

            if (Run != null && State != GameState.GameOver)
            {
                FinishRun(false);
            }

            Time.timeScale = 1f;
            Run = null;
            ReviveOfferActive = false;
            ResetAdFlowState();
            StartAttract();
            SetState(GameState.Menu);
        }

        // ------------------------------------------------------------------ crash, revive, results

        /// <summary>
        /// Forgets in-flight ad flows of the previous crash / results screen, so their (late) callbacks can no
        /// longer revive, freeze the offer timer of, or navigate away from a later run.
        /// </summary>
        private void ResetAdFlowState()
        {
            _adFlowGeneration++;
            _adPausedOffer = false;
            _leavingResults = false;
        }

        private void OnCrashed()
        {
            _crashTimer = 0f;
            ResetAdFlowState();
            Time.timeScale = CrashSlowMotionScale;
            SetState(GameState.Crashed);
        }

        private void TickCrashed(float unscaled)
        {
            if (Ads != null && Ads.IsShowing)
            {
                return;
            }

            // The first frame after the app returns from the background can carry a huge unscaled delta; don't
            // let it expire the continue offer the player never got to see.
            unscaled = Mathf.Min(unscaled, MaxOfferTimerStep);
            _crashTimer += unscaled;
            if (_crashTimer >= CrashSlowMotionSeconds && Time.timeScale < 1f && !_adPausedOffer)
            {
                Time.timeScale = 1f;
            }

            if (!ReviveOfferActive)
            {
                if (_crashTimer >= CrashToOfferSeconds)
                {
                    if (Run != null && Run.CanRevive && (CanReviveWithAd || CanReviveWithGems))
                    {
                        ReviveOfferActive = true;
                        ReviveOfferRemaining = ReviveOfferSeconds;
                    }
                    else
                    {
                        FinishRun(true);
                    }
                }

                return;
            }

            if (!_adPausedOffer)
            {
                ReviveOfferRemaining -= unscaled;
                if (ReviveOfferRemaining <= 0f)
                {
                    DeclineRevive();
                }
            }
        }

        /// <summary>Continue by watching a rewarded ad.</summary>
        public void ReviveWithAd(Action<bool> onDone = null)
        {
            // _adPausedOffer: one request at a time — a second one would be refused by the ad service and its
            // "not completed" answer would unfreeze the offer timer while the first ad is still on its way.
            if (!ReviveOfferActive || Ads == null || _adPausedOffer)
            {
                onDone?.Invoke(false);
                return;
            }

            int generation = _adFlowGeneration;
            _adPausedOffer = true;
            Ads.ShowRewarded(completed =>
            {
                if (generation != _adFlowGeneration)
                {
                    // Stale: the player left this crash (quit, restart) before the ad reported back. Never let
                    // it revive a later crash for free.
                    onDone?.Invoke(false);
                    return;
                }

                _adPausedOffer = false;
                if (completed && ReviveOfferActive)
                {
                    DoRevive();
                }
                else if (ReviveOfferActive)
                {
                    // Give them a few more seconds to choose after a skipped/failed ad.
                    ReviveOfferRemaining = Mathf.Max(ReviveOfferRemaining, 3f);
                }

                onDone?.Invoke(completed);
            });
        }

        /// <summary>Continue by spending gems. Returns false if the player can't afford it.</summary>
        public bool ReviveWithGems()
        {
            if (!ReviveOfferActive || !Profile.TrySpend(ReviveGemCost))
            {
                PresentError();
                return false;
            }

            DoRevive();
            return true;
        }

        public void DeclineRevive()
        {
            if (State != GameState.Crashed)
            {
                return;
            }

            ReviveOfferActive = false;
            FinishRun(true);
        }

        private void DoRevive()
        {
            ReviveOfferActive = false;
            Time.timeScale = 1f;
            if (Run != null && Run.Revive())
            {
                DispatchRunEvents();
                Input.ResetGestures();
                BeginCountdown(ResumeCountdownStep);
            }
            else
            {
                FinishRun(true);
            }
        }

        /// <summary>Runs abandoned (restart/quit) before this distance are dropped instead of counted.</summary>
        private const float AbandonedRunDistance = 50f;

        private void FinishRun(bool showResults)
        {
            if (Run == null)
            {
                return;
            }

            Time.timeScale = 1f;
            ReviveOfferActive = false;
            RunResult result = Run.BuildResult();
            if (!showResults && result.Distance < AbandonedRunDistance)
            {
                // Restarted or quit before really flying (e.g. during the 3-2-1): not a run. Counting it let
                // PLAY → pause → RESTART loops (~1 s each) farm "play N runs" missions and run-count achievements,
                // and advanced interstitial pacing.
                LastSummary = null;
                return;
            }

            LastSummary = Profile.ApplyRun(result);
            ApplyDailyRunResult(LastSummary);
            Store.SaveNow(true);
            _pendingInterstitial = Profile.ShouldShowInterstitial(DateTime.UtcNow);
            if (showResults)
            {
                SetState(GameState.GameOver);
                PresentResults(LastSummary);
            }

            try
            {
                RunFinished?.Invoke(LastSummary);
            }
            catch (Exception e)
            {
                // Callers (QuitToMenu, RestartRun) continue after FinishRun; aborting them here would leave the
                // finished run in place, and a second quit would apply it to the profile twice.
                FieldLog.Exception("RunFinished handler", e);
            }
        }

        private void RaiseCountdownTicked(int value)
        {
            try
            {
                CountdownTicked?.Invoke(value);
            }
            catch (Exception e)
            {
                // A throwing handler would otherwise skip the switch to Running on every tick: stuck at 3-2-1.
                FieldLog.Exception("CountdownTicked handler", e);
            }
        }

        /// <summary>Results screen "double gems" button (rewarded ad).</summary>
        public void DoubleGemsWithAd(Action<bool> onDone)
        {
            if (LastSummary == null || LastSummary.GemsDoubled || Ads == null)
            {
                onDone?.Invoke(false);
                return;
            }

            RunSummary summary = LastSummary;
            Ads.ShowRewarded(completed =>
            {
                if (completed)
                {
                    Profile.DoubleRunGems(summary);
                    Store.SaveNow(true);
                    PresentReward();
                }

                onDone?.Invoke(completed);
            });
        }

        /// <summary>Leaves the results screen: retry or menu, possibly via a (rate-limited) interstitial.</summary>
        public void ContinueFromResults(bool retry)
        {
            // _leavingResults: an interstitial is on its way. Without this guard a second request (the Android
            // back key / Escape goes through GameUI straight to here, bypassing the results screen's own
            // button lock) proceeded immediately, and the interstitial's callback later proceeded AGAIN —
            // restarting the new run mid-countdown, or FinishRun-ing it via QuitToMenu.
            if (State != GameState.GameOver || _leavingResults)
            {
                return;
            }

            if (_pendingInterstitial && Ads != null && Ads.Enabled && Ads.InterstitialReady)
            {
                _pendingInterstitial = false;
                _leavingResults = true;
                int generation = _adFlowGeneration;
                Ads.ShowInterstitial(() =>
                {
                    if (generation != _adFlowGeneration)
                    {
                        return;
                    }

                    _leavingResults = false;
                    Profile.MarkInterstitialShown(DateTime.UtcNow);
                    if (State == GameState.GameOver)
                    {
                        LeaveResults(retry);
                    }
                });
                return;
            }

            LeaveResults(retry);
        }

        private void LeaveResults(bool retry)
        {
            if (retry)
            {
                StartRun();
            }
            else
            {
                QuitToMenu();
            }
        }

        // ------------------------------------------------------------------ hangar & meta commands

        public void EnterHangar()
        {
            if (State != GameState.Menu)
            {
                return;
            }

            PreviewShipId = Profile.Profile.SelectedShip;
            SetState(GameState.Hangar);
        }

        public void ExitHangar()
        {
            if (State != GameState.Hangar)
            {
                return;
            }

            PreviewShipId = Profile.Profile.SelectedShip;
            SetState(GameState.Menu);
        }

        /// <summary>Shows a ship on the hangar turntable without selecting it.</summary>
        public void PreviewShip(string shipId)
        {
            if (!ShipCatalog.Exists(shipId) || shipId == PreviewShipId)
            {
                return;
            }

            PreviewShipId = shipId;
            PresentPreviewShip(shipId);
        }

        public ShipPurchaseResult BuyShip(string shipId)
        {
            ShipPurchaseResult result = Profile.BuyShip(shipId);
            if (result == ShipPurchaseResult.Purchased)
            {
                Profile.SelectShip(shipId);
                Store.SaveNow(true);
                PresentPurchase();
                PresentSelectedShip();
            }
            else if (result != ShipPurchaseResult.AlreadyOwned)
            {
                PresentError();
            }

            return result;
        }

        public bool SelectShip(string shipId)
        {
            bool ok = Profile.SelectShip(shipId);
            if (ok)
            {
                PresentSelectedShip();
            }

            return ok;
        }

        public int ClaimDailyReward()
        {
            int amount = Profile.ClaimDailyReward();
            if (amount > 0)
            {
                Store.SaveNow(true);
                PresentReward();
            }

            return amount;
        }

        /// <summary>Re-applies every setting (call after the settings screen changes something).</summary>
        public void ApplySettings()
        {
            PlayerSettings s = Profile.Settings;
            Input.Mode = s.ControlMode;
            Input.Sensitivity = s.Sensitivity;
            Input.InvertY = s.InvertY;
            Haptics.Enabled = s.Haptics;

            ApplySettingsPresentation(s);
            Store.MarkDirty();
        }

        public void ResetProgress()
        {
            Store.ResetProgress();
            PreviewShipId = Profile.Profile.SelectedShip;
            PresentSelectedShip();
        }

        private void AutoDetectGraphicsQuality()
        {
            PlayerSettings s = Profile.Settings;
            if (s.GraphicsQuality >= 0)
            {
                return;
            }

            int memory = SystemInfo.systemMemorySize;
            int cores = SystemInfo.processorCount;
            if (!Application.isMobilePlatform)
            {
                s.GraphicsQuality = 2;
            }
            else if (memory < 3000 || cores <= 4)
            {
                s.GraphicsQuality = 0;
            }
            else if (memory < 6000)
            {
                s.GraphicsQuality = 1;
            }
            else
            {
                s.GraphicsQuality = 2;
            }

            Store.MarkDirty();
        }

        private void RaiseNotify(string message)
        {
            Notify?.Invoke(message);
        }

        private void RaiseConsentRequested(Action<bool> callback)
        {
            if (ConsentRequested != null)
            {
                ConsentRequested(callback);
            }
            else
            {
                callback(false);
            }
        }

        // ------------------------------------------------------------------ extension points
        // Implemented in GameController.Presentation.cs / .Services.cs / .UI.cs.

        partial void BuildPresentation();

        partial void TickPresentation(float dt, float unscaledDt);

        partial void OnStatePresentation(GameState from, GameState to);

        partial void OnSimulationReplaced();

        partial void PresentRunEvent(RunEvent e);

        partial void PresentAttractEvent(RunEvent e);

        partial void PresentCountdown(int value);

        partial void PresentMissionComplete(Mission mission);

        partial void PresentResults(RunSummary summary);

        partial void PresentPreviewShip(string shipId);

        partial void PresentSelectedShip();

        partial void PresentPurchase();

        partial void PresentReward();

        partial void PresentError();

        partial void ApplySettingsPresentation(PlayerSettings settings);

        partial void PresentUiSound(SfxId id);

        partial void CreateUI();

        partial void InitializeServices();

        // Test probe (GameController.Probe.cs; Editor and development builds only, compiled out otherwise).
        partial void ProbeAttach();

        partial void ProbeDetach();

        partial void ProbeOverrideInput(ref RunInput input);

        private bool PresentationReady()
        {
            bool ready = true;
            QueryPresentationReady(ref ready);
            return ready;
        }

        private float PresentationLoadProgress()
        {
            float progress = 1f;
            QueryPresentationProgress(ref progress);
            return progress;
        }

        partial void QueryPresentationReady(ref bool ready);

        partial void QueryPresentationProgress(ref float progress);
    }
}

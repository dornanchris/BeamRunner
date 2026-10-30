using System.Collections.Generic;

namespace BeamRunner.Core.Diagnostics
{
    /// <summary>
    /// Test/debug view of the running game: read-only state plus commands that go through the same code the UI buttons
    /// call (each command names the button it stands for). Published by the Unity layer through
    /// <see cref="GameProbeRegistry"/> in the Editor and development builds only. Main thread only.
    /// <para>Commands return false when they were refused (wrong state, button hidden or not interactable, …) — the
    /// game's own guards decide, exactly as for a real tap. Screen names are UI type names (e.g. "MainMenuScreen",
    /// "PauseScreen", "SettingsScreen", "ResultsScreen", "ReviveScreen", "DailyRewardScreen", "SplashScreen").</para>
    /// </summary>
    public interface IGameProbe
    {
        // ------------------------------------------------------------------ flow & UI

        /// <summary>The controller's state: Boot, Menu, Hangar, Countdown, Running, Paused, Crashed or GameOver.</summary>
        string FlowState { get; }

        /// <summary>True once boot finished (the menu can start runs).</summary>
        bool IsBootComplete { get; }

        /// <summary>Type name of the screen on top of the UI stack, or "" when the stack is empty (e.g. during a run).</summary>
        string TopScreen { get; }

        /// <summary>Type names of every visible screen, on the stack or not (splash, HUD, daily popup, countdown…).</summary>
        IReadOnlyList<string> VisibleScreens { get; }

        /// <summary>True while a modal dialog is open (consent, quit, confirmations).</summary>
        bool DialogOpen { get; }

        /// <summary>Button labels of the open dialog (as displayed), empty when none is open.</summary>
        IReadOnlyList<string> DialogOptions { get; }

        /// <summary>Labels of every visible, interactable labelled button (diagnostics).</summary>
        IReadOnlyList<string> PressableButtons { get; }

        /// <summary>True while a UI transition or fade is still running.</summary>
        bool UiTransitioning { get; }

        // ------------------------------------------------------------------ profile

        long Gems { get; }

        int RunsPlayed { get; }

        long BestScore { get; }

        bool TutorialDone { get; }

        /// <summary>The player answered the ads-consent dialog.</summary>
        bool ConsentAsked { get; }

        bool DailyRewardAvailable { get; }

        /// <summary>Absolute path of the profile file, or "" when the storage is not file based.</summary>
        string SaveFilePath { get; }

        // ------------------------------------------------------------------ run

        bool HasRun { get; }

        bool RunIsTutorial { get; }

        double RunDistance { get; }

        long RunScore { get; }

        /// <summary>Level the current run is in (1-based; 0 without a run).</summary>
        int RunLevel { get; }

        bool RunCrashed { get; }

        int RevivesUsed { get; }

        bool ReviveOfferActive { get; }

        /// <summary>A (simulated) full-screen ad is on screen.</summary>
        bool AdShowing { get; }

        /// <summary>UnityEngine.Time.timeScale.</summary>
        float TimeScale { get; }

        /// <summary>Who flies the player's ship (applies from the next frame; ignored outside runs).</summary>
        AutopilotMode Autopilot { get; set; }

        // ------------------------------------------------------------------ logs

        /// <summary>Errors, exceptions and failed asserts logged since the probe was attached (any thread).</summary>
        int ErrorCount { get; }

        int WarningCount { get; }

        /// <summary>The first logged errors/exceptions (message + first stack line), for failure messages.</summary>
        IReadOnlyList<string> Errors { get; }

        /// <summary>One-line summary of everything above, for assertion messages.</summary>
        string Describe();

        // ------------------------------------------------------------------ commands

        /// <summary>
        /// Clicks the visible, interactable button labelled <paramref name="label"/> (case-insensitive; the top-most UI
        /// layer wins: dialogs, popups, HUD, screens) through uGUI's pointer-click path.
        /// </summary>
        bool PressButton(string label);

        /// <summary>Consent dialog: PERSONALISED ADS / NON-PERSONALISED.</summary>
        bool AnswerConsent(bool personalised);

        /// <summary>Daily reward popup: CLAIM.</summary>
        bool ClaimDailyReward();

        /// <summary>Main menu gift button.</summary>
        bool OpenDailyReward();

        /// <summary>Main menu PLAY (on the results screen use <see cref="ContinueFromResults"/> for RETRY).</summary>
        bool StartRun();

        /// <summary>HUD pause button.</summary>
        bool PauseRun();

        /// <summary>Pause screen RESUME.</summary>
        bool ResumeRun();

        /// <summary>Main menu settings button / pause screen SETTINGS.</summary>
        bool OpenSettings();

        /// <summary>Main menu MISSIONS.</summary>
        bool OpenMissions();

        /// <summary>Main menu RECORDS.</summary>
        bool OpenRecords();

        /// <summary>Main menu HANGAR.</summary>
        bool EnterHangar();

        /// <summary>Hangar back button.</summary>
        bool ExitHangar();

        /// <summary>Back / close button of the top overlay (meta screens, settings, daily popup).</summary>
        bool CloseTop();

        /// <summary>Escape / Android back / gamepad B (the UI stack's back routing, including dialogs).</summary>
        bool Back();

        /// <summary>Revive offer WATCH AD.</summary>
        bool ReviveWithAd();

        /// <summary>Revive offer ◆ gems button.</summary>
        bool ReviveWithGems();

        /// <summary>Revive offer NO THANKS.</summary>
        bool DeclineRevive();

        /// <summary>Simulated ad overlay: CLOSE (<paramref name="watchedToEnd"/>, only once its countdown ended) or SKIP.</summary>
        bool FinishSimulatedAd(bool watchedToEnd);

        /// <summary>Results screen RETRY (<paramref name="retry"/>) or MENU.</summary>
        bool ContinueFromResults(bool retry);

        /// <summary>Writes the profile now (what the game does when the app is paused or quits).</summary>
        void FlushSave();

        // ------------------------------------------------------------------ daily run & ghost

        /// <summary>The Daily Run is unlocked (first flight done): the main menu offers DAILY RUN.</summary>
        bool DailyRunAvailable { get; }

        /// <summary>Main menu DAILY RUN (today's UTC track). On the results screen RETRY keeps a Daily Run a Daily Run.</summary>
        bool StartDailyRun();

        /// <summary>The current run — or, on its results screen, the last run — is a Daily Run.</summary>
        bool IsDailyRun { get; }

        /// <summary>The ghost of today's best Daily Run is drawn in front of the camera.</summary>
        bool GhostVisible { get; }

        /// <summary>Today's best Daily Run score (0 when none was counted today, UTC).</summary>
        long DailyBestScore { get; }

        /// <summary>Current Daily Run streak (consecutive UTC days; 0 once a day was missed).</summary>
        int DailyStreak { get; }

        /// <summary>The results screen is visible and shows its Daily Run panel (daily best / new best / streak).</summary>
        bool ResultsShowDaily { get; }

        // ------------------------------------------------------------------ language

        /// <summary>Code of the language the UI shows ("en", "fr", "zh-Hans", "qps-ploc"…).</summary>
        string Language { get; }

        /// <summary>
        /// Settings ▸ LANGUAGE picker: switches to <paramref name="code"/> (null or "" = AUTOMATIC, the device language).
        /// The UI rebuilds behind a fade once the menu is idle (<see cref="UiTransitioning"/> meanwhile). False for an
        /// unknown code or outside the menu/hangar (the picker is hidden during runs).
        /// </summary>
        bool SetLanguage(string code);
    }
}

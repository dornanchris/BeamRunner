using System.Collections;
using System.IO;
using BeamRunner.Core.Diagnostics;
using BeamRunner.Core.Meta;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BeamRunner.PlayModeTests
{
    /// <summary>
    /// One-click version of much of docs/FIRST_RUN_CHECKLIST.md: boots the real Main scene and drives the game through
    /// its flows via the game's test probe, failing on any error or exception the game logs. Each test runs a fresh
    /// app launch with its own temporary save (the player's real save is never touched), simulated ads and no store.
    /// Run from Window ▸ General ▸ Test Runner ▸ PlayMode. Don't touch keyboard/mouse/gamepad while they run.
    /// </summary>
    [Category("Smoke")]
    public sealed class GameSmokeTests : SmokeTestBase
    {
        private const int LongTestTimeoutMs = 300000;

        [UnityTest, Timeout(LongTestTimeoutMs)]
        public IEnumerator FirstLaunch_SplashConsentAndDailyReward_ReachMenu()
        {
            yield return LaunchGame();

            // Splash while booting.
            bool sawSplash = false;
            yield return WaitFor(() =>
            {
                if (Game.FlowState != "Boot")
                {
                    return true;
                }

                sawSplash |= IsVisible(Splash);
                return false;
            }, BootTimeout, "boot to finish");
            Assert.IsTrue(sawSplash, "The splash screen was never visible during boot. " + Describe());
            Assert.AreEqual("Menu", Game.FlowState, "Boot must end on the menu. " + Describe());
            Assert.IsTrue(Game.IsBootComplete, Describe());
            Assert.IsFalse(Game.ConsentAsked, "A fresh profile must not have answered consent yet. " + Describe());

            // First launch: consent dialog (both choices offered), then the daily reward.
            yield return WaitFor(() => Game.DialogOpen, UiTimeout, "the ads-consent dialog on first launch");
            CollectionAssert.IsSupersetOf(Game.DialogOptions, new[] { "PERSONALISED ADS", "NON-PERSONALISED" },
                "The consent dialog must offer both choices. " + Describe());
            yield return AnswerConsent();
            Assert.IsTrue(File.Exists(SaveFile), "Answering consent must save the profile into the test folder. " + Describe());
            string used = Game.SaveFilePath;
            Assert.IsFalse(string.IsNullOrEmpty(used), "The game's save storage is not file based. " + Describe());
            Assert.AreEqual(Path.GetFullPath(SaveFile), Path.GetFullPath(used),
                "The game does not use the test's save folder. " + Describe());

            yield return ClaimDailyReward();
            yield return WaitForMenuIdle();
            Assert.AreEqual(1f, Game.TimeScale, Describe());
            AssertNoGameErrors("idling on the menu");
        }

        [UnityTest, Timeout(LongTestTimeoutMs)]
        public IEnumerator FirstRun_TutorialWithBot_CrashDeclineRevive_ResultsToMenu()
        {
            yield return BootToMenu(true);
            Assert.AreEqual(0, Game.RunsPlayed, Describe());

            yield return StartRun();
            Assert.IsTrue(Game.RunIsTutorial, "The first run must be the tutorial. " + Describe());

            // Fly most of level 1, past its first gaps (> 300 m also completes the tutorial).
            Assert.Greater(FirstRunMetres, 300.0, "Level 1 is too short for the tutorial check below.");
            yield return FlyWithBot(FirstRunMetres);
            Assert.Greater(Game.RunScore, 0L, "No score after " + FirstRunMetres + " m. " + Describe());
            Assert.AreEqual(1, Game.RunLevel, "The first run starts at level 1 (the bot stops short of its wall). " + Describe());

            yield return CrashIntoTheVoid();
            yield return WaitFor(() => Game.ReviveOfferActive, UiTimeout, "the revive offer after the crash");
            yield return WaitFor(() => Game.TopScreen == Revive, UiTimeout, "the revive offer screen");
            Assert.AreEqual(1f, Game.TimeScale, "Slow motion must end before the offer. " + Describe());

            Assert.IsTrue(Game.DeclineRevive(), "NO THANKS was refused. " + Describe());
            Assert.AreEqual("GameOver", Game.FlowState, Describe());
            Assert.AreEqual(1, Game.RunsPlayed, "The run was not counted. " + Describe());
            Assert.IsTrue(Game.TutorialDone, "A first run past 300 m must complete the tutorial. " + Describe());
            Assert.Greater(Game.BestScore, 0L, Describe());

            yield return ResultsToMenu();
        }

        [UnityTest, Timeout(LongTestTimeoutMs)]
        public IEnumerator Run_ReviveWithSimulatedAd_ThenSecondCrashEndsRun()
        {
            yield return BootToMenu(true);
            yield return StartRun();
            yield return CrashIntoTheVoid();
            yield return WaitFor(() => Game.ReviveOfferActive, UiTimeout, "the revive offer");

            // WATCH AD → simulated ad overlay (3 s countdown) → CLOSE → revived with a short 3-2-1.
            yield return WaitFor(() => Game.ReviveWithAd(), UiTimeout, "WATCH AD on the revive offer");
            yield return WaitFor(() => Game.AdShowing, UiTimeout, "the simulated rewarded ad to open");
            yield return WaitFor(() => Game.FinishSimulatedAd(true), UiTimeout, "the simulated ad's CLOSE button to unlock");
            yield return WaitFor(() => !Game.AdShowing, UiTimeout, "the simulated ad to close");
            yield return WaitForState("Running", UiTimeout);
            Assert.AreEqual(1, Game.RevivesUsed, "Watching the ad did not revive. " + Describe());
            Assert.IsFalse(Game.RunCrashed, Describe());

            // Second crash: no revives left, straight to the results.
            yield return CrashIntoTheVoid();
            yield return WaitFor(() => Game.FlowState == "GameOver", UiTimeout, "the results after the second crash");
            Assert.IsFalse(Game.ReviveOfferActive, "A second revive must not be offered. " + Describe());
            Assert.AreEqual(1, Game.RunsPlayed, Describe());

            yield return ResultsToMenu();
        }

        [UnityTest, Timeout(LongTestTimeoutMs)]
        public IEnumerator Run_PauseSettingsBackResume_ThenQuitToMenu()
        {
            yield return BootToMenu(true);
            yield return StartRun();
            yield return FlyWithBot(60);

            // HUD pause button → pause screen, run frozen.
            Assert.IsTrue(Game.PauseRun(), "Pause was refused. " + Describe());
            Assert.AreEqual(0f, Game.TimeScale, "Paused runs must freeze time. " + Describe());
            yield return WaitFor(() => Game.TopScreen == Pause, UiTimeout, "the pause screen");
            double frozenAt = Game.RunDistance;
            yield return HoldFor(0.5f, () => Game.RunDistance == frozenAt && Game.FlowState == "Paused", "the run to stay frozen while paused");

            // SETTINGS from pause, back with the settings screen's back button...
            yield return WaitForUiIdle();
            Assert.IsTrue(Game.OpenSettings(), "SETTINGS from the pause screen was refused. " + Describe());
            yield return WaitForUiIdle();
            Assert.IsTrue(Game.CloseTop(), "The settings back button was refused. " + Describe());
            yield return WaitFor(() => Game.TopScreen == Pause, UiTimeout, "the pause screen after closing settings");
            Assert.AreEqual("Paused", Game.FlowState, Describe());

            // ...and again with Escape / Android back.
            yield return WaitForUiIdle();
            Assert.IsTrue(Game.OpenSettings(), Describe());
            yield return WaitForUiIdle();
            Assert.IsTrue(Game.Back(), "Back on the settings screen was refused. " + Describe());
            yield return WaitFor(() => Game.TopScreen == Pause, UiTimeout, "the pause screen after Back from settings");

            // RESUME → short 3-2-1 → running again.
            yield return WaitForUiIdle();
            Assert.IsTrue(Game.ResumeRun(), "RESUME was refused. " + Describe());
            yield return WaitForState("Running", UiTimeout);
            Assert.AreEqual(1f, Game.TimeScale, Describe());
            double resumedAt = Game.RunDistance;
            yield return WaitFor(() => Game.RunDistance > resumedAt + 10.0, UiTimeout, "the run to move again after resuming");

            // Back while paused resumes too.
            Assert.IsTrue(Game.PauseRun(), Describe());
            yield return WaitFor(() => Game.TopScreen == Pause, UiTimeout, "the pause screen");
            yield return WaitSeconds(0.2f);
            Assert.IsTrue(Game.Back(), "Back on the pause screen was refused. " + Describe());
            yield return WaitForState("Running", UiTimeout);

            // MENU on the pause screen asks first; LEAVE ends the run (it still counts).
            Assert.IsTrue(Game.PauseRun(), Describe());
            yield return WaitFor(() => Game.TopScreen == Pause, UiTimeout, "the pause screen");
            yield return WaitForUiIdle();
            Assert.IsTrue(Game.PressButton("MENU"), "MENU on the pause screen was refused. " + Describe());
            yield return WaitFor(() => Game.DialogOptions.Contains("LEAVE"), UiTimeout, "the quit-to-menu confirmation");
            yield return WaitFor(() => Game.PressButton("LEAVE"), UiTimeout, "LEAVE on the quit-to-menu confirmation");
            yield return WaitForState("Menu", UiTimeout);
            yield return WaitForMenuIdle();
            Assert.AreEqual(1, Game.RunsPlayed, "An abandoned run must still count. " + Describe());
        }

        [UnityTest, Timeout(LongTestTimeoutMs)]
        public IEnumerator Menu_EveryMetaScreenOpensAndCloses()
        {
            yield return BootToMenu(true);

            // Hangar: back via Escape, then via its back button; the gem store (unavailable here) opens and closes.
            Assert.IsTrue(Game.EnterHangar(), Describe());
            yield return WaitFor(() => Game.TopScreen == "HangarScreen", UiTimeout, "the hangar");
            yield return WaitForUiIdle();
            yield return WaitSeconds(0.5f);
            Assert.IsTrue(Game.Back(), "Back in the hangar was refused. " + Describe());
            yield return WaitForState("Menu", UiTimeout);
            yield return WaitForMenuIdle();

            Assert.IsTrue(Game.EnterHangar(), Describe());
            yield return WaitForUiIdle();
            Assert.IsTrue(Game.PressButton("GEM STORE"), "GEM STORE in the hangar was refused. " + Describe());
            yield return WaitSeconds(1f);
            Assert.IsTrue(Game.Back(), "Back must close the gem store. " + Describe());
            Assert.AreEqual("Hangar", Game.FlowState, "Back with the store open must only close the store. " + Describe());
            yield return WaitSeconds(0.5f);
            Assert.IsTrue(Game.ExitHangar(), "The hangar back button was refused. " + Describe());
            yield return WaitForMenuIdle();

            // Overlays from the menu.
            Assert.IsTrue(Game.OpenMissions(), "MISSIONS was refused. " + Describe());
            yield return WaitForUiIdle();
            yield return WaitSeconds(0.3f);
            Assert.IsTrue(Game.CloseTop(), "The missions back button was refused. " + Describe());
            yield return WaitForMenuIdle();

            Assert.IsTrue(Game.OpenRecords(), "RECORDS was refused. " + Describe());
            yield return WaitForUiIdle();
            yield return WaitSeconds(0.3f);
            Assert.IsTrue(Game.Back(), "Back on the records screen was refused. " + Describe());
            yield return WaitForMenuIdle();

            Assert.IsTrue(Game.OpenSettings(), "The settings button was refused. " + Describe());
            yield return WaitForUiIdle();
            yield return WaitSeconds(0.3f);
            Assert.IsTrue(Game.CloseTop(), "The settings back button was refused. " + Describe());
            yield return WaitForMenuIdle();

            // The gift button re-opens the (already claimed) daily reward popup.
            Assert.IsTrue(Game.OpenDailyReward(), "The gift button was refused. " + Describe());
            yield return WaitForUiIdle();
            Assert.IsFalse(Game.DailyRewardAvailable, Describe());
            Assert.IsTrue(Game.CloseTop(), "Closing the daily reward popup was refused. " + Describe());
            yield return WaitForMenuIdle();

            // Back at the root of the menu asks to quit; STAY keeps playing.
            yield return WaitSeconds(0.2f);
            Assert.IsTrue(Game.Back(), "Back on the main menu was refused. " + Describe());
            yield return WaitFor(() => Game.DialogOptions.Contains("STAY"), UiTimeout, "the quit confirmation");
            yield return WaitFor(() => Game.PressButton("STAY"), UiTimeout, "STAY on the quit confirmation");
            yield return WaitForMenuIdle();
        }

        [UnityTest, Timeout(LongTestTimeoutMs)]
        public IEnumerator Settings_LanguageSwitch_RebuildsTheUiWithoutRestart()
        {
            yield return BootToMenu(true);
            Assert.AreEqual("en", Game.Language, "Smoke tests run in English. " + Describe());

            // Settings ▸ LANGUAGE ▸ FRANÇAIS: the UI rebuilds behind a fade and Settings stays open.
            Assert.IsTrue(Game.OpenSettings(), "The settings button was refused. " + Describe());
            yield return WaitForUiIdle();
            Assert.IsTrue(Game.SetLanguage("fr"), "Switching to French was refused. " + Describe());
            yield return WaitForUiIdle();
            Assert.AreEqual("fr", Game.Language, Describe());
            Assert.AreEqual("SettingsScreen", Game.TopScreen, "Settings must stay open across the rebuild. " + Describe());
            Assert.IsTrue(Game.CloseTop(), "The settings back button was refused after the rebuild. " + Describe());
            yield return WaitFor(() => Game.TopScreen == MainMenu && !Game.UiTransitioning && Game.PressableButtons.Contains("JOUER"),
                UiTimeout, "the main menu in French (JOUER)");
            Assert.IsFalse(Game.PressableButtons.Contains("PLAY"), "English labels survived the rebuild. " + Describe());

            // Unknown codes are refused; AUTOMATIC goes back to the (test) device language.
            Assert.IsFalse(Game.SetLanguage("xx"), Describe());
            Assert.IsTrue(Game.SetLanguage(null), "Switching back to automatic was refused. " + Describe());
            yield return WaitForMenuIdle();
            Assert.AreEqual("en", Game.Language, Describe());
            Assert.AreEqual(0, Game.ErrorCount, "Errors were logged: " + string.Join(" | ", Game.Errors) + " " + Describe());
        }

        [UnityTest, Timeout(LongTestTimeoutMs)]
        public IEnumerator SecondLaunch_ExistingSave_BootsStraightToMenu()
        {
            yield return BootToMenu(true);
            long gems = Game.Gems;
            Game.FlushSave();
            Assert.IsTrue(File.Exists(SaveFile), "No save file after the first launch. " + Describe());

            yield return ShutdownGame();
            Assert.IsNull(GameProbeRegistry.Current, "The game survived the shutdown.");

            yield return LaunchGame();
            yield return WaitForState("Menu", BootTimeout);
            Assert.IsTrue(Game.ConsentAsked, "The consent answer was lost. " + Describe());
            Assert.AreEqual(gems, Game.Gems, "The claimed gems were lost. " + Describe());
            Assert.IsFalse(Game.DailyRewardAvailable, "Today's reward was already claimed. " + Describe());

            // Neither the consent dialog nor the daily popup may appear (the popup's auto-open runs ~1 s after the menu).
            yield return HoldFor(3f, () => !Game.DialogOpen && !IsVisible(DailyReward),
                "no consent dialog and no daily reward popup on a second launch");
            yield return WaitForMenuIdle();
        }

        [UnityTest, Timeout(LongTestTimeoutMs)]
        public IEnumerator Save_WrittenToTestFolder_AndReloadedAfterRun()
        {
            yield return BootToMenu(true);
            yield return StartRun();
            yield return CrashIntoTheVoid();
            yield return WaitFor(() => Game.ReviveOfferActive || Game.FlowState == "GameOver", UiTimeout,
                "the revive offer or the results");
            if (Game.FlowState == "Crashed")
            {
                Assert.IsTrue(Game.DeclineRevive(), Describe());
            }

            yield return ResultsToMenu();

            int runs = Game.RunsPlayed;
            long best = Game.BestScore;
            long gems = Game.Gems;
            bool tutorialDone = Game.TutorialDone;
            Assert.AreEqual(1, runs, Describe());

            // The file on disk (written at the end of the run) holds the run.
            Assert.IsTrue(File.Exists(SaveFile), "No save file in the test folder after a run. " + Describe());
            // The file is a signed envelope around the profile JSON (SaveEnvelope); an edited copy would not verify.
            string payload;
            string installId;
            SaveDocumentKind kind = SaveEnvelope.Open(File.ReadAllText(SaveFile), SaveIntegrityKey.Get(), null, out payload,
                out installId);
            Assert.AreEqual(SaveDocumentKind.Signed, kind, "The save file is not a correctly signed document. " + Describe());
            PlayerProfile saved = JsonUtility.FromJson<PlayerProfile>(payload);
            Assert.IsNotNull(saved, "The save file does not parse. " + Describe());
            Assert.AreEqual(runs, saved.Stats.Runs, "The saved profile misses the run. " + Describe());
            Assert.AreEqual(best, saved.Stats.BestScore, Describe());
            Assert.IsTrue(saved.Flags.ConsentAsked, Describe());

            // Relaunch: everything comes back.
            yield return ShutdownGame();
            yield return LaunchGame();
            yield return WaitForState("Menu", BootTimeout);
            Assert.AreEqual(runs, Game.RunsPlayed, "Runs were not reloaded. " + Describe());
            Assert.AreEqual(best, Game.BestScore, "The best score was not reloaded. " + Describe());
            Assert.AreEqual(gems, Game.Gems, "Gems were not reloaded. " + Describe());
            Assert.AreEqual(tutorialDone, Game.TutorialDone, Describe());
            Assert.IsTrue(Game.ConsentAsked, Describe());
            yield return WaitForMenuIdle();

            // The reloaded profile plays: the next run is a tutorial only if the first one did not complete it.
            yield return StartRun();
            Assert.AreEqual(!tutorialDone, Game.RunIsTutorial, Describe());
        }
    }
}

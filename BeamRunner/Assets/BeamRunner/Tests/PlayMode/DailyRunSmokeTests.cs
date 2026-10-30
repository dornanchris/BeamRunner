using System.Collections;
using BeamRunner.Core.Diagnostics;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace BeamRunner.PlayModeTests
{
    /// <summary>
    /// Daily Run end to end in the real game: locked until the first flight, today's track without tutorial or revive,
    /// the results' Daily Run panel with the day's best, and the ghost of that best run flying along on the next attempt.
    /// (Runs on the real UTC clock: started within a minute of 00:00 UTC, the second attempt may land on a new day.)
    /// </summary>
    [Category("Smoke")]
    public sealed class DailyRunSmokeTests : SmokeTestBase
    {
        private const int TimeoutMs = 300000;
        private const string DailyRunButton = "DAILY RUN";

        [UnityTest, Timeout(TimeoutMs)]
        public IEnumerator DailyRun_ResultsShowTheDailyBest_AndTheGhostFliesOnTheSecondAttempt()
        {
            yield return BootToMenu(true);
            Assert.IsFalse(Game.DailyRunAvailable, "The Daily Run must stay locked until the first flight. " + Describe());
            Assert.IsFalse(Game.StartDailyRun(), "DAILY RUN started before the first flight. " + Describe());
            Assert.IsFalse(Game.PressableButtons.Contains(DailyRunButton), "The menu shows DAILY RUN while locked. " + Describe());

            // First flight (the tutorial) unlocks it.
            yield return StartRun();
            yield return FlyWithBot(FirstRunMetres);
            yield return CrashIntoTheVoid();
            yield return WaitFor(() => Game.ReviveOfferActive || Game.FlowState == "GameOver", UiTimeout, "the revive offer or the results");
            if (Game.FlowState == "Crashed")
            {
                Assert.IsTrue(Game.DeclineRevive(), Describe());
            }

            Assert.IsFalse(Game.IsDailyRun, "A PLAY run is not a Daily Run. " + Describe());
            yield return ResultsToMenu();
            Assert.IsTrue(Game.DailyRunAvailable, "The first flight must unlock the Daily Run. " + Describe());
            yield return WaitFor(() => Game.PressableButtons.Contains(DailyRunButton), UiTimeout, "the DAILY RUN button on the menu");

            // Attempt 1: today's track, no tutorial, no ghost yet, and no revive offer after the crash.
            yield return StartDailyRun();
            Assert.IsFalse(Game.RunIsTutorial, Describe());
            yield return FlyWithBot(250);
            Assert.IsFalse(Game.GhostVisible, "No ghost before today's first Daily Run was recorded. " + Describe());
            yield return CrashIntoTheVoid();
            yield return WaitFor(() =>
            {
                Assert.IsFalse(Game.ReviveOfferActive, "The Daily Run must not offer a revive. " + Describe());
                return Game.FlowState == "GameOver";
            }, UiTimeout, "the results straight after the Daily Run crash");
            Assert.AreEqual(0, Game.RevivesUsed, Describe());
            Assert.IsTrue(Game.IsDailyRun, "The results belong to a Daily Run. " + Describe());
            long firstBest = Game.DailyBestScore;
            Assert.Greater(firstBest, 0L, "The Daily Run was not recorded as today's best. " + Describe());
            Assert.AreEqual(1, Game.DailyStreak, Describe());
            yield return WaitFor(() => Game.TopScreen == Results && Game.ResultsShowDaily, UiTimeout, "the results' Daily Run panel");
            yield return WaitSeconds(1f);

            // Attempt 2 through RETRY: still the Daily Run, and the ghost of attempt 1 flies along.
            Assert.IsTrue(Game.ContinueFromResults(true), "RETRY was refused. " + Describe());
            yield return WaitForState("Running", UiTimeout);
            Assert.IsTrue(Game.IsDailyRun, "RETRY after a Daily Run must start another Daily Run. " + Describe());
            Game.Autopilot = AutopilotMode.FollowLine;
            yield return WaitFor(() => Game.GhostVisible, UiTimeout, "the ghost of the first attempt");
            yield return FlyWithBot(150);
            Assert.IsTrue(Game.GhostVisible, "The ghost must keep flying alongside. " + Describe());
            yield return CrashIntoTheVoid();
            yield return WaitFor(() => Game.FlowState == "GameOver", UiTimeout, "the results of the second attempt");
            yield return WaitFor(() => Game.TopScreen == Results && Game.ResultsShowDaily, UiTimeout, "the results' Daily Run panel");
            Assert.GreaterOrEqual(Game.DailyBestScore, firstBest, "Today's best can only go up. " + Describe());
            Assert.AreEqual(1, Game.DailyStreak, "A second attempt the same day does not extend the streak. " + Describe());
            Assert.IsFalse(Game.GhostVisible, "The ghost hides on the results. " + Describe());

            yield return ResultsToMenu();
            Assert.IsTrue(Game.PressableButtons.Contains(DailyRunButton), Describe());
        }

        private IEnumerator StartDailyRun()
        {
            Assert.IsTrue(Game.StartDailyRun(), "DAILY RUN was refused. " + Describe());
            Assert.IsTrue(Game.IsDailyRun, Describe());
            yield return WaitFor(() => IsVisible(Hud), UiTimeout, "the HUD to appear for the 3-2-1");
            yield return WaitForState("Running", UiTimeout);
        }
    }
}

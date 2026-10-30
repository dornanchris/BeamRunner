using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BeamRunner.Core.Diagnostics;
using BeamRunner.Core.Field;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

namespace BeamRunner.PlayModeTests
{
    /// <summary>
    /// Shared plumbing of the PlayMode smoke tests: an isolated save folder per test (<see cref="TestLaunchOptions"/>,
    /// simulated ads, no store), loading the real Main scene, restarting the game inside one play session, and
    /// polling waits with generous timeouts whose failure messages carry the game's probe state. Any error or
    /// exception the game logs fails the test: the Test Framework's log scope does that on the next frame, and every
    /// wait here checks the probe's error counter first so the message says where the game was.
    /// </summary>
    public abstract class SmokeTestBase
    {
        protected const string MainScenePath = "Assets/BeamRunner/Scenes/Main.unity";

        /// <summary>Must match <c>FileSaveStorage.DefaultFileName</c> (Assembly-CSharp, not referenceable from tests).</summary>
        protected const string SaveFileName = "beamrunner_profile.json";

        protected const float BootTimeout = 60f;
        protected const float UiTimeout = 10f;
        protected const float RunTimeout = 60f;

        protected const string MainMenu = "MainMenuScreen";
        protected const string Splash = "SplashScreen";
        protected const string DailyReward = "DailyRewardScreen";
        protected const string Hud = "HudScreen";
        protected const string Pause = "PauseScreen";
        protected const string Revive = "ReviveScreen";
        protected const string Results = "ResultsScreen";

        private const string SandboxRootName = "BeamRunnerSmokeTests";
        private static readonly TimeSpan StaleSandboxAge = TimeSpan.FromHours(6);

        private static int s_emptyScenes;

        private string _saveDirectory;
        private string _realSaveFingerprint;

        /// <summary>The running game's probe; fails the test when no game is running.</summary>
        protected IGameProbe Game
        {
            get
            {
                IGameProbe probe = GameProbeRegistry.Current;
                if (probe == null)
                {
                    Assert.Fail("The game is not running (no probe registered). Did the Main scene load?");
                }

                return probe;
            }
        }

        /// <summary>This test's private save folder.</summary>
        protected string SaveDirectory
        {
            get { return _saveDirectory; }
        }

        protected string SaveFile
        {
            get { return Path.Combine(_saveDirectory, SaveFileName); }
        }

        // ------------------------------------------------------------------ lifecycle

        [SetUp]
        public void CreateSandbox()
        {
            string root = Path.Combine(Path.GetTempPath(), SandboxRootName);
            DeleteStaleSandboxes(root);
            _saveDirectory = Path.Combine(root, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_saveDirectory);
            _realSaveFingerprint = RealSaveFingerprint();
            ApplyLaunchOptions();
        }

        [UnityTearDown]
        public IEnumerator TearDownGame()
        {
            yield return ShutdownGame();
            TestLaunchOptions.Clear();
            TryDeleteDirectory(_saveDirectory);
            Assert.AreEqual(_realSaveFingerprint, RealSaveFingerprint(),
                "The player's real save in Application.persistentDataPath changed during a smoke test.");
        }

        // ------------------------------------------------------------------ launching

        /// <summary>Loads the Main scene (a fresh launch: the previous game must have been shut down).</summary>
        protected IEnumerator LaunchGame()
        {
            Assert.IsNull(GameProbeRegistry.Current, "A game is still running: " + Describe());
            ApplyLaunchOptions(); // (again: nothing between SetUp and here may leave the real save reachable)
            AsyncOperation load = LoadMainScene();
            Assert.IsNotNull(load, "Could not start loading " + MainScenePath + ".");
            while (!load.isDone)
            {
                yield return null;
            }

            yield return WaitFor(() => GameProbeRegistry.Current != null, UiTimeout,
                "the game controller to start (probe registration) after loading " + MainScenePath);
        }

        /// <summary>
        /// Restart emulation: unloads Main (every game object is destroyed normally), destroys the game's
        /// DontDestroyOnLoad helpers and re-runs its static resets, so the next <see cref="LaunchGame"/> boots like a
        /// new app launch. Safe to call when nothing is running. Never fails the test.
        /// </summary>
        protected IEnumerator ShutdownGame()
        {
            IGameProbe probe = GameProbeRegistry.Current;
            if (probe != null)
            {
                probe.Autopilot = AutopilotMode.Off;
            }

            Time.timeScale = 1f;
            AudioListener.pause = false;

            Scene main = SceneManager.GetSceneByPath(MainScenePath);
            if (main.IsValid() && main.isLoaded)
            {
                s_emptyScenes++;
                Scene empty = SceneManager.CreateScene("BeamRunnerSmokeEmpty" + s_emptyScenes.ToString(CultureInfo.InvariantCulture));
                SceneManager.SetActiveScene(empty);
                AsyncOperation unload = SceneManager.UnloadSceneAsync(main);
                if (unload != null)
                {
                    while (!unload.isDone)
                    {
                        yield return null;
                    }
                }
            }

            IAppResetHooks hooks = GameProbeRegistry.AppResetHooks;
            if (hooks != null)
            {
                hooks.DestroyPersistentObjects();
                yield return null;
                hooks.ResetStatics();
            }

            yield return null;
        }

        /// <summary>
        /// Launches and waits for the menu. <paramref name="firstLaunch"/>: also answers the consent dialog
        /// (non-personalised) and claims + closes the daily reward, checking each appears.
        /// </summary>
        protected IEnumerator BootToMenu(bool firstLaunch)
        {
            yield return LaunchGame();
            yield return WaitForState("Menu", BootTimeout);
            if (firstLaunch)
            {
                yield return AnswerConsent();
                yield return ClaimDailyReward();
            }

            yield return WaitForMenuIdle();
        }

        protected IEnumerator AnswerConsent()
        {
            yield return WaitFor(() => Game.DialogOptions.Contains("NON-PERSONALISED"), UiTimeout, "the ads-consent dialog");
            yield return WaitFor(() => Game.AnswerConsent(false), UiTimeout, "the consent dialog to accept NON-PERSONALISED");
            yield return WaitFor(() => !Game.DialogOpen, UiTimeout, "the consent dialog to close");
            Assert.IsTrue(Game.ConsentAsked, "The consent answer was not recorded. " + Describe());
        }

        protected IEnumerator ClaimDailyReward()
        {
            yield return WaitFor(() => IsVisible(DailyReward), UiTimeout, "the daily reward popup to open by itself");
            Assert.IsTrue(Game.DailyRewardAvailable, "Daily popup shown without an available reward. " + Describe());
            yield return WaitSeconds(0.5f); // let the popup animate in (and uGUI see the menu's CanvasGroup lock)
            Assert.IsFalse(Game.PressableButtons.Contains("PLAY"),
                "The menu behind the daily reward popup must be locked. " + Describe());
            long before = Game.Gems;
            yield return WaitFor(() => Game.ClaimDailyReward(), UiTimeout, "CLAIM on the daily reward popup to pay out");
            Assert.Greater(Game.Gems, before, "Claiming the daily reward added no gems. " + Describe());
            Assert.IsFalse(Game.DailyRewardAvailable, "The daily reward can be claimed twice. " + Describe());
            yield return WaitSeconds(0.5f);
            yield return WaitFor(() => Game.CloseTop(), UiTimeout, "the daily reward popup to close");
            yield return WaitFor(() => !IsVisible(DailyReward), UiTimeout, "the daily reward popup to disappear");
        }

        /// <summary>Menu on top, nothing covering it, no transition running, PLAY usable.</summary>
        protected IEnumerator WaitForMenuIdle()
        {
            yield return WaitFor(() => Game.FlowState == "Menu" && Game.TopScreen == MainMenu && !Game.DialogOpen &&
                                       !IsVisible(DailyReward) && !IsVisible(Splash) && !Game.UiTransitioning &&
                                       Game.PressableButtons.Contains("PLAY"),
                UiTimeout, "an idle main menu with a usable PLAY button");
        }

        protected IEnumerator WaitForUiIdle()
        {
            yield return WaitFor(() => !Game.UiTransitioning, UiTimeout, "UI transitions to finish");
        }

        // ------------------------------------------------------------------ runs

        /// <summary>PLAY, then the 3-2-1 until the player has control.</summary>
        protected IEnumerator StartRun()
        {
            Assert.IsTrue(Game.StartRun(), "StartRun was refused. " + Describe());
            yield return WaitFor(() => IsVisible(Hud), UiTimeout, "the HUD to appear for the 3-2-1");
            yield return WaitForState("Running", UiTimeout);
            Assert.AreEqual(1f, Game.TimeScale, "Runs must play at normal speed. " + Describe());
            Assert.IsTrue(IsVisible(Hud), "The HUD is not visible during the run. " + Describe());
        }

        /// <summary>Lets the line-following bot fly until the run reaches <paramref name="metres"/>.</summary>
        protected IEnumerator FlyWithBot(double metres)
        {
            Game.Autopilot = AutopilotMode.FollowLine;
            yield return WaitFor(() => Game.RunDistance >= metres || Game.FlowState != "Running", RunTimeout,
                "the autopilot to fly " + metres.ToString("F0", CultureInfo.InvariantCulture) + " m");
            Assert.AreEqual("Running", Game.FlowState,
                "The line-following autopilot did not survive " + metres.ToString("F0", CultureInfo.InvariantCulture) + " m. " + Describe());
        }

        /// <summary>
        /// Metres the bot flies on a first run: past the 300 m that complete the tutorial, and far enough short of level 1's
        /// wall (whose checkpoint waits for the pilot) for <see cref="CrashIntoTheVoid"/> to dive two layers before it.
        /// </summary>
        protected static readonly double FirstRunMetres = FieldSpec.TileStart(new Course(1).WallTile(1) - 5);

        /// <summary>Builds ramps down until the craft drops off the bottom layer and the run crashes.</summary>
        protected IEnumerator CrashIntoTheVoid()
        {
            Game.Autopilot = AutopilotMode.SeekCrash;
            yield return WaitFor(() => Game.FlowState == "Crashed", RunTimeout, "the ship to fall into the void");
            Assert.IsTrue(Game.RunCrashed, "Crashed state without a crashed run. " + Describe());
            Assert.Less(Game.TimeScale, 1f, "The crash should start in slow motion. " + Describe());
            Game.Autopilot = AutopilotMode.Off;
        }

        /// <summary>From the results screen back to an idle menu through MENU.</summary>
        protected IEnumerator ResultsToMenu()
        {
            yield return WaitFor(() => Game.TopScreen == Results, UiTimeout, "the results screen");
            yield return WaitSeconds(1f); // let the results animations (count-ups, banners) run
            Assert.IsTrue(Game.ContinueFromResults(false), "MENU on the results screen was refused. " + Describe());
            yield return WaitForState("Menu", UiTimeout);
            yield return WaitForMenuIdle();
        }

        // ------------------------------------------------------------------ waiting

        /// <summary>Polls <paramref name="condition"/> every frame (real time; the game changes timeScale).</summary>
        protected IEnumerator WaitFor(Func<bool> condition, float timeoutSeconds, string what)
        {
            float deadline = Time.realtimeSinceStartup + timeoutSeconds;
            while (true)
            {
                AssertNoGameErrors("waiting for " + what);
                if (condition())
                {
                    yield break;
                }

                if (Time.realtimeSinceStartup > deadline)
                {
                    Assert.Fail("Timed out after " + timeoutSeconds.ToString("0.#", CultureInfo.InvariantCulture) +
                                " s waiting for " + what + ".\n" + Describe());
                }

                yield return null;
            }
        }

        protected IEnumerator WaitForState(string state, float timeoutSeconds)
        {
            yield return WaitFor(() => Game.FlowState == state, timeoutSeconds, "state " + state);
        }

        /// <summary>Waits real seconds, failing early on game errors.</summary>
        protected IEnumerator WaitSeconds(float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end)
            {
                AssertNoGameErrors("waiting " + seconds.ToString("0.#", CultureInfo.InvariantCulture) + " s");
                yield return null;
            }
        }

        /// <summary>Waits real seconds while <paramref name="invariant"/> must hold on every frame.</summary>
        protected IEnumerator HoldFor(float seconds, Func<bool> invariant, string what)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end)
            {
                AssertNoGameErrors("checking that " + what);
                Assert.IsTrue(invariant(), "Expected " + what + ".\n" + Describe());
                yield return null;
            }
        }

        protected void AssertNoGameErrors(string context)
        {
            IGameProbe probe = GameProbeRegistry.Current;
            if (probe != null && probe.ErrorCount > 0)
            {
                Assert.Fail("The game logged " + probe.ErrorCount + " error(s) while " + context + ":\n  " +
                            string.Join("\n  ", probe.Errors) + "\n" + probe.Describe());
            }
        }

        protected bool IsVisible(string screen)
        {
            return Game.VisibleScreens.Contains(screen);
        }

        protected static string Describe()
        {
            IGameProbe probe = GameProbeRegistry.Current;
            return probe != null ? probe.Describe() : "[probe] no game running";
        }

        // ------------------------------------------------------------------ helpers

        private void ApplyLaunchOptions()
        {
            Assert.IsFalse(string.IsNullOrEmpty(_saveDirectory), "No sandbox folder (SetUp did not run).");
            TestLaunchOptions.SaveDirectory = _saveDirectory;
            TestLaunchOptions.OfflineServices = true;
            TestLaunchOptions.Language = "en"; // buttons are pressed by their English labels, whatever the machine's language
        }

        private static AsyncOperation LoadMainScene()
        {
#if UNITY_EDITOR
            // Editor: no Build Settings entry needed.
            return EditorSceneManager.LoadSceneAsyncInPlayMode(MainScenePath, new LoadSceneParameters(LoadSceneMode.Single));
#else
            return SceneManager.LoadSceneAsync(MainScenePath, LoadSceneMode.Single);
#endif
        }

        /// <summary>Size + write time of the real profile (or "absent"): reading metadata only.</summary>
        private static string RealSaveFingerprint()
        {
            try
            {
                var file = new FileInfo(Path.Combine(Application.persistentDataPath, SaveFileName));
                return file.Exists
                    ? file.Length.ToString(CultureInfo.InvariantCulture) + "@" + file.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture)
                    : "absent";
            }
            catch (Exception e)
            {
                return "unreadable: " + e.GetType().Name;
            }
        }

        private static void DeleteStaleSandboxes(string root)
        {
            try
            {
                if (!Directory.Exists(root))
                {
                    return;
                }

                foreach (string dir in Directory.GetDirectories(root))
                {
                    if (DateTime.UtcNow - Directory.GetLastWriteTimeUtc(dir) > StaleSandboxAge)
                    {
                        TryDeleteDirectory(dir);
                    }
                }
            }
            catch (Exception)
            {
                // Best effort: leftovers of an aborted run are harmless.
            }
        }

        private static void TryDeleteDirectory(string dir)
        {
            try
            {
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                {
                    Directory.Delete(dir, true);
                }
            }
            catch (Exception)
            {
                // Best effort (a virus scanner may briefly hold a file on Windows).
            }
        }
    }

    internal static class ReadOnlyListExtensions
    {
        public static bool Contains(this IReadOnlyList<string> list, string value)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i], value, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}

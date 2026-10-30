using System;
using BeamRunner.Core.Diagnostics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace BeamRunner.EditorTools
{
    /// <summary>
    /// Editor-load guard: keeps the main scene in Build Settings (silently, logged once per session), warns once
    /// if URP is not the default pipeline (never changes pipelines itself), and optionally makes Play Mode always
    /// start from the main scene (menu toggle, persisted in EditorPrefs, on by default).
    /// </summary>
    [InitializeOnLoad]
    public static class FieldEditorStartup
    {
        private const string PlayFromMainMenu = FieldEditorConstants.MenuRoot + "Play Mode Starts From Main Scene";
        private const string PlayFromMainPref = "BeamRunner.PlayModeStartsFromMain";
        private const string SceneLoggedKey = "BeamRunner.StartupSceneLogged";
        private const string UrpWarnedKey = "BeamRunner.StartupUrpWarned";
        private const string TestScenePathPrefix = "Assets/InitTestScene";
        private const string TestRunnerObjectName = "Code-based tests runner";

        static FieldEditorStartup()
        {
            EditorApplication.delayCall += OnEditorReady;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorSceneManager.activeSceneChangedInEditMode += OnActiveSceneChanged;
        }

        /// <summary>Whether entering Play Mode from another scene should load the main scene instead.</summary>
        public static bool PlayFromMain
        {
            get => EditorPrefs.GetBool(PlayFromMainPref, true);
            set
            {
                EditorPrefs.SetBool(PlayFromMainPref, value);
                RefreshPlayModeStartScene();
            }
        }

        private static void OnEditorReady()
        {
            if (Application.isBatchMode)
            {
                // CI/headless: only make sure the scene list is right; no warnings spam, no play-mode wiring.
                FieldProjectSetup.EnsureMainSceneFirstInBuild(out _);
                return;
            }

            try
            {
                GuardBuildSettings();
                GuardRenderPipeline();
                Menu.SetChecked(PlayFromMainMenu, PlayFromMain);
                RefreshPlayModeStartScene();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        private static void GuardBuildSettings()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(FieldEditorConstants.MainScenePath) == null)
            {
                return;   // Validate Project / Rebuild Main Scene report this; stay quiet on load.
            }

            bool inBuild = false;
            foreach (EditorBuildSettingsScene s in EditorBuildSettings.scenes)
            {
                if (s.enabled && string.Equals(s.path, FieldEditorConstants.MainScenePath, StringComparison.Ordinal))
                {
                    inBuild = true;
                    break;
                }
            }

            if (inBuild)
            {
                return;
            }

            FieldProjectSetup.EnsureMainSceneFirstInBuild(out _);
            if (!SessionState.GetBool(SceneLoggedKey, false))
            {
                SessionState.SetBool(SceneLoggedKey, true);
                Debug.Log("[BeamRunner] Added " + FieldEditorConstants.MainScenePath + " to Build Settings (scene 0).");
            }
        }

        private static void GuardRenderPipeline()
        {
            if (FieldProjectSetup.IsUrp(GraphicsSettings.defaultRenderPipeline) || SessionState.GetBool(UrpWarnedKey, false))
            {
                return;
            }

            SessionState.SetBool(UrpWarnedKey, true);
            Debug.LogWarning("[BeamRunner] URP is not the default render pipeline, so bloom and post-processing " +
                             "are disabled. Run 'BeamRunner/Setup Project' to assign " +
                             FieldEditorConstants.UrpAssetPath + ".");
        }

        [MenuItem(PlayFromMainMenu, false, 100)]
        private static void TogglePlayFromMain()
        {
            PlayFromMain = !PlayFromMain;
            Menu.SetChecked(PlayFromMainMenu, PlayFromMain);
        }

        [MenuItem(PlayFromMainMenu, true)]
        private static bool TogglePlayFromMainValidate()
        {
            Menu.SetChecked(PlayFromMainMenu, PlayFromMain);
            return !EditorApplication.isPlayingOrWillChangePlaymode;
        }

        private static void OnActiveSceneChanged(Scene previous, Scene next)
        {
            RefreshPlayModeStartScene();
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingEditMode)
            {
                RefreshPlayModeStartScene();
            }
            else if (change == PlayModeStateChange.EnteredEditMode)
            {
                // A PlayMode smoke test stopped half-way never ran its teardown; with domain reload disabled its launch
                // overrides (temp save folder, offline services) would otherwise apply to the next normal Play.
                TestLaunchOptions.Clear();
            }
        }

        /// <summary>
        /// Points <see cref="EditorSceneManager.playModeStartScene"/> at the main scene when the toggle is on and a
        /// different scene is active; clears it otherwise (so edits to an open Main scene are played unsaved).
        /// Also cleared while the Test Runner's bootstrap scene is active: a PlayMode test run enters Play Mode from its
        /// own scene, whose runner object must be loaded (with a start scene set, Unity would load Main instead and
        /// the run would wait forever for its controller). The tests load Main themselves.
        /// </summary>
        public static void RefreshPlayModeStartScene()
        {
            if (Application.isBatchMode || EditorApplication.isPlaying)
            {
                return;
            }

            SceneAsset target = null;
            if (PlayFromMain)
            {
                Scene active = SceneManager.GetActiveScene();
                bool mainIsActive = active.IsValid() &&
                                    string.Equals(active.path, FieldEditorConstants.MainScenePath, StringComparison.Ordinal);
                if (!mainIsActive && !IsTestRunnerScene(active))
                {
                    target = AssetDatabase.LoadAssetAtPath<SceneAsset>(FieldEditorConstants.MainScenePath);
                }
            }

            if (EditorSceneManager.playModeStartScene != target)
            {
                EditorSceneManager.playModeStartScene = target;
            }
        }

        /// <summary>
        /// True for the Unity Test Framework's PlayMode bootstrap scene: saved as <c>Assets/InitTestScene&lt;guid&gt;.unity</c>
        /// with a root object named "Code-based tests runner" (a name the framework documents as stable for third-party
        /// code). Both are checked so a future naming change of either one still keeps test runs working.
        /// </summary>
        private static bool IsTestRunnerScene(Scene scene)
        {
            if (!scene.IsValid())
            {
                return false;
            }

            if (scene.path.StartsWith(TestScenePathPrefix, StringComparison.Ordinal))
            {
                return true;
            }

            if (!scene.isLoaded)
            {
                return false;
            }

            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                if (roots[i] != null && string.Equals(roots[i].name, TestRunnerObjectName, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}

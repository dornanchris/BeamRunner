using BeamRunner.Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace BeamRunner.EditorTools
{
    /// <summary>
    /// <c>BeamRunner/Open Main Scene</c> and <c>BeamRunner/Rebuild Main Scene</c>. The main scene holds a
    /// single <c>BeamRunner</c> GameObject with <see cref="GameBootstrap"/>; everything else is built in code.
    /// </summary>
    public static class FieldSceneTools
    {
        /// <summary>Near-black violet flat ambient (the game is unlit; this only affects stray lit materials).</summary>
        public static readonly Color AmbientColor = new Color(0.02f, 0.012f, 0.035f, 1f);

        [MenuItem(FieldEditorConstants.MenuRoot + "Open Main Scene", false, 20)]
        private static void OpenMainScene()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(FieldEditorConstants.MainScenePath) == null)
            {
                if (EditorUtility.DisplayDialog("BeamRunner",
                        "The main scene is missing (" + FieldEditorConstants.MainScenePath + ").\n\nRebuild it now?",
                        "Rebuild", "Cancel"))
                {
                    RebuildMainScene(false);
                }

                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            EditorSceneManager.OpenScene(FieldEditorConstants.MainScenePath, OpenSceneMode.Single);
        }

        [MenuItem(FieldEditorConstants.MenuRoot + "Rebuild Main Scene", false, 21)]
        private static void RebuildMainSceneMenu()
        {
            bool exists = AssetDatabase.LoadAssetAtPath<SceneAsset>(FieldEditorConstants.MainScenePath) != null;
            if (exists && !EditorUtility.DisplayDialog("BeamRunner — Rebuild Main Scene",
                    "Recreate " + FieldEditorConstants.MainScenePath + " from scratch?\n\n" +
                    "It will contain only the '" + FieldEditorConstants.BootstrapObjectName +
                    "' GameObject with GameBootstrap (the scene GUID is kept).",
                    "Rebuild", "Cancel"))
            {
                return;
            }

            RebuildMainScene(true);
        }

        /// <summary>
        /// Creates the main scene (empty scene + bootstrap object + dark render settings), saves it to
        /// <see cref="FieldEditorConstants.MainScenePath"/> and puts it first in Build Settings. Returns success.
        /// </summary>
        public static bool RebuildMainScene(bool showResult)
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return false;
            }

            string folder = System.IO.Path.GetDirectoryName(FieldEditorConstants.MainScenePath);
            if (!string.IsNullOrEmpty(folder) && !AssetDatabase.IsValidFolder(folder.Replace('\\', '/')))
            {
                System.IO.Directory.CreateDirectory(folder);
                AssetDatabase.Refresh();
            }

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var root = new GameObject(FieldEditorConstants.BootstrapObjectName);
            root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            root.transform.localScale = Vector3.one;
            root.AddComponent<GameBootstrap>();

            ApplyRenderSettings();

            bool saved = EditorSceneManager.SaveScene(scene, FieldEditorConstants.MainScenePath);
            if (!saved)
            {
                Debug.LogError("[BeamRunner] Could not save " + FieldEditorConstants.MainScenePath);
                if (showResult)
                {
                    EditorUtility.DisplayDialog("BeamRunner", "Saving the main scene failed. See the Console.", "OK");
                }

                return false;
            }

            AssetDatabase.Refresh();
            FieldProjectSetup.EnsureMainSceneFirstInBuild(out string problem);
            if (problem != null)
            {
                Debug.LogWarning("[BeamRunner] " + problem);
            }

            FieldEditorStartup.RefreshPlayModeStartScene();
            Debug.Log("[BeamRunner] Rebuilt " + FieldEditorConstants.MainScenePath + " (first in Build Settings).");
            if (showResult)
            {
                EditorUtility.DisplayDialog("BeamRunner", "Main scene rebuilt and set as scene 0 in Build Settings.", "OK");
            }

            return true;
        }

        /// <summary>Scene lighting for the main scene: no skybox, no fog, flat near-black ambient.</summary>
        public static void ApplyRenderSettings()
        {
            RenderSettings.skybox = null;
            RenderSettings.fog = false;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = AmbientColor;
            RenderSettings.ambientSkyColor = AmbientColor;
            RenderSettings.ambientEquatorColor = AmbientColor;
            RenderSettings.ambientGroundColor = AmbientColor;
            RenderSettings.sun = null;
        }
    }
}

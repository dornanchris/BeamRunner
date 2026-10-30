using UnityEngine;

namespace BeamRunner.Game
{
    /// <summary>
    /// Scene entry point. The Main scene contains a single GameObject with this component; everything
    /// else (camera, world, UI, audio, services) is created in code by <see cref="GameController"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GameBootstrap : MonoBehaviour
    {
        private void Awake()
        {
            if (GameController.Instance != null)
            {
                // A controller already exists (e.g. duplicate bootstrap): keep the first one.
                Destroy(gameObject);
                return;
            }

            gameObject.name = "BeamRunner";
            if (GetComponent<GameController>() == null)
            {
                gameObject.AddComponent<GameController>();
            }
        }

        /// <summary>
        /// Safety net: if the game is started from an empty scene (no bootstrap, no camera — e.g. the scene
        /// reference was lost), boot anyway so the build never shows a blank screen.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureBooted()
        {
            if (GameController.Instance != null)
            {
                return;
            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // PlayMode tests start in the Test Framework's empty scene and load the main scene themselves, with an
            // isolated save (TestLaunchOptions): never boot there with the player's real profile. (The Test Framework
            // documents this object name as the way to recognise its runner.)
            if (GameObject.Find("Code-based tests runner") != null)
            {
                return;
            }
#endif

            if (Object.FindAnyObjectByType<GameBootstrap>() != null || Object.FindAnyObjectByType<Camera>() != null)
            {
                return;
            }

            new GameObject("BeamRunner").AddComponent<GameBootstrap>();
        }
    }
}

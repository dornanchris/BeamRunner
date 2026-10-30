namespace BeamRunner.Core.Diagnostics
{
    /// <summary>
    /// Lets a test emulate an app restart inside one play session: after the main scene has been unloaded, call
    /// <see cref="DestroyPersistentObjects"/>, wait a frame, then <see cref="ResetStatics"/>; the next load of the main
    /// scene then boots like a fresh launch. Registered by the Unity layer (Editor / development builds) through
    /// <see cref="GameProbeRegistry.AppResetHooks"/>.
    /// </summary>
    public interface IAppResetHooks
    {
        /// <summary>Destroys the game's objects that survive scene loads (DontDestroyOnLoad helpers, ad overlays).</summary>
        void DestroyPersistentObjects();

        /// <summary>
        /// Re-runs the game's static initialisers (its <c>[RuntimeInitializeOnLoadMethod]</c> resets, as Unity does when
        /// entering Play Mode with domain reload disabled), so service singletons, caches and hooks start over.
        /// </summary>
        void ResetStatics();
    }
}

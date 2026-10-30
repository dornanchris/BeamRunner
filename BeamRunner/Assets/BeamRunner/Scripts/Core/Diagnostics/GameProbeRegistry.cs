namespace BeamRunner.Core.Diagnostics
{
    /// <summary>
    /// Where the running game publishes its <see cref="IGameProbe"/> (and its restart hooks) for automated tests, which
    /// can only reference this pure C# assembly. The Unity layer registers the probe when the game controller wakes up
    /// and unregisters it when the controller is destroyed — in the Editor and development builds only. Main thread.
    /// </summary>
    public static class GameProbeRegistry
    {
        /// <summary>The probe of the running game, or null (no game running, or a release build).</summary>
        public static IGameProbe Current { get; private set; }

        /// <summary>Restart hooks of the game's code (stays set across restarts once the game ran once).</summary>
        public static IAppResetHooks AppResetHooks { get; private set; }

        /// <summary>Publishes <paramref name="probe"/> as <see cref="Current"/>.</summary>
        public static void Register(IGameProbe probe)
        {
            Current = probe;
        }

        /// <summary>Withdraws <paramref name="probe"/> if it is still the current one.</summary>
        public static void Unregister(IGameProbe probe)
        {
            if (ReferenceEquals(Current, probe))
            {
                Current = null;
            }
        }

        /// <summary>Publishes the game's restart hooks.</summary>
        public static void RegisterAppResetHooks(IAppResetHooks hooks)
        {
            AppResetHooks = hooks;
        }
    }
}

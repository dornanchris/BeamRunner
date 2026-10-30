namespace BeamRunner.Core.Diagnostics
{
    /// <summary>
    /// Launch-time overrides for automated tests, set by the test <b>before</b> it loads the main scene and cleared
    /// afterwards (<see cref="Clear"/>). Only the Editor and development builds honour them (the Unity layer reads them
    /// inside <c>#if UNITY_EDITOR || DEVELOPMENT_BUILD</c>), so a release build can never be redirected.
    /// <list type="bullet">
    /// <item><see cref="SaveDirectory"/>: the profile (progress + settings, one JSON document with its <c>.tmp</c> /
    /// <c>.bak</c> companions) lives there instead of <c>Application.persistentDataPath</c> — the player's real save is
    /// never read or written. This also bypasses the WebGL <c>PlayerPrefs</c> profile storage.</item>
    /// <item><see cref="OfflineServices"/>: ads are always the local simulation and the store is the null service (no
    /// network, no purchase dialogs, and the IAP grant ledger — the only other <c>PlayerPrefs</c> key the game writes —
    /// is never created).</item>
    /// <item><see cref="Language"/>: the device language for this launch (e.g. "en", so the smoke tests can press
    /// buttons by their English labels whatever the machine's language). A Settings choice still applies. Not
    /// stored.</item>
    /// </list>
    /// </summary>
    public static class TestLaunchOptions
    {
        /// <summary>Folder for the profile; null/empty = the normal location.</summary>
        public static string SaveDirectory { get; set; }

        /// <summary>Simulated ads + no store, whatever the monetization config or platform says.</summary>
        public static bool OfflineServices { get; set; }

        /// <summary>
        /// Stands in for the device language (e.g. "en"); null/empty = the real one. A choice made in Settings (in-memory
        /// DevicePrefs while a test override is active) still wins, so tests can switch languages.
        /// </summary>
        public static string Language { get; set; }

        /// <summary>True while any override is set.</summary>
        public static bool IsActive
        {
            get { return !string.IsNullOrEmpty(SaveDirectory) || OfflineServices || !string.IsNullOrEmpty(Language); }
        }

        /// <summary>Back to a normal launch.</summary>
        public static void Clear()
        {
            SaveDirectory = null;
            OfflineServices = false;
            Language = null;
        }
    }
}

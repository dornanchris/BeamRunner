namespace BeamRunner.Game
{
    /// <summary>
    /// "Background mode" for clean footage recordings (design doc, Footage and marketing): build with the scripting define
    /// <c>BEAMRUNNER_FOOTAGE</c> (Player Settings ▸ Other Settings ▸ Scripting Define Symbols) and
    /// every run is flown by the autopilot, never crashes, and the HUD shows nothing but a small game-name watermark.
    /// The menu still works (to pick a ship or a start level). Never ship a store build with the define set.
    /// </summary>
    public static class FootageMode
    {
#if BEAMRUNNER_FOOTAGE
        /// <summary>True in footage builds.</summary>
        public static readonly bool Enabled = true;
#else
        /// <summary>True in footage builds.</summary>
        public static readonly bool Enabled = false;
#endif
    }
}

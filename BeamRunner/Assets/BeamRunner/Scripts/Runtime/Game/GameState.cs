namespace BeamRunner.Game
{
    /// <summary>Top-level flow of the app. See docs/ARCHITECTURE.md §7.</summary>
    public enum GameState
    {
        /// <summary>Services, audio and visuals are being prepared (splash/loading screen).</summary>
        Boot = 0,

        /// <summary>Main menu with the autopilot "attract" run in the background.</summary>
        Menu,

        /// <summary>Ship showcase / shop.</summary>
        Hangar,

        /// <summary>3-2-1 before a run starts or resumes.</summary>
        Countdown,

        /// <summary>Player in control.</summary>
        Running,

        /// <summary>Run frozen, pause menu open.</summary>
        Paused,

        /// <summary>Just crashed: slow-motion, then the continue offer.</summary>
        Crashed,

        /// <summary>Results screen.</summary>
        GameOver,

        /// <summary>At the top of a level wall: stats and a perk pick before the next level.</summary>
        Checkpoint,
    }
}

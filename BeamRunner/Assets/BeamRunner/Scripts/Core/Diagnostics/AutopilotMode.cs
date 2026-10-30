namespace BeamRunner.Core.Diagnostics
{
    /// <summary>Who flies the player's ship while a <see cref="IGameProbe"/> drives the game.</summary>
    public enum AutopilotMode
    {
        /// <summary>The player's input (touch, keyboard, gamepad) as usual.</summary>
        Off = 0,

        /// <summary>Plays like the menu's attract bot: picks lanes and builds what the route ahead needs.</summary>
        FollowLine = 1,

        /// <summary>Builds ramps down until the craft drops into the void.</summary>
        SeekCrash = 2,
    }
}

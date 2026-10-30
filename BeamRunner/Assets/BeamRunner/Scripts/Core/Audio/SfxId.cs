namespace BeamRunner.Core.Audio
{
    /// <summary>Every one-shot sound effect in the game. Rendered procedurally by the synth.</summary>
    public enum SfxId
    {
        UiClick = 0,
        UiBack,
        UiToggle,
        UiError,
        Purchase,
        Reward,
        CountdownTick,
        CountdownGo,
        GemPickup,
        PowerUpPickup,
        NearMiss,
        GatePass,
        GateSmash,
        ShieldUp,
        ShieldBreak,
        MagnetStart,
        OverdriveReady,
        OverdriveStart,
        OverdriveEnd,
        SlipstreamEnter,
        ZoneChange,
        Milestone,
        MultiplierUp,
        Crash,
        Revive,
        NewRecord,
        MissionComplete,
        RankUp,

        /// <summary>One auto-fire volley (short laser zap; played quietly and rate-limited).</summary>
        Shot,

        /// <summary>A shot lands on a block.</summary>
        BlockHit,

        /// <summary>A block breaks apart.</summary>
        BlockBreak,

        /// <summary>A shot glances off armor or a closed gate slot.</summary>
        ShotPing,
    }

    /// <summary>Which music arrangement is playing.</summary>
    public enum MusicMode
    {
        Silent = 0,
        Menu,
        Run,
        GameOver,
    }
}

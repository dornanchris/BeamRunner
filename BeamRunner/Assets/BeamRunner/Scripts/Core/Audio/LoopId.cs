namespace BeamRunner.Core.Audio
{
    /// <summary>Seamless ambience loops whose pitch/volume the game modulates continuously.</summary>
    public enum LoopId
    {
        /// <summary>Ship engine hum (detuned saws + filtered noise); pitch/filter follow speed.</summary>
        Engine = 0,

        /// <summary>Airy shimmering wind: the rush of Overdrive (named after the predecessor game's slipstream).</summary>
        Slipstream,
    }
}

namespace BeamRunner.Core.Audio
{
    /// <summary>A layer of a music arrangement. All stems of an arrangement have identical length and loop together.</summary>
    public enum MusicStem
    {
        Drums = 0,
        Bass,
        Pads,
        Arp,
        Lead,

        /// <summary>
        /// Playback layout only: every always-on layer of an arrangement (see <see cref="MusicLayers.IsFaded"/>)
        /// summed into one clip, e.g. drums + bass + pads of the Run arrangement, or the whole Menu arrangement.
        /// </summary>
        Base,
    }
}

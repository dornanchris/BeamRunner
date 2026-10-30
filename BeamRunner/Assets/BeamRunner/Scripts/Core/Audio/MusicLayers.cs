namespace BeamRunner.Core.Audio
{
    /// <summary>
    /// Which music stems the game fades on their own: only the Run intensity layers (arp, lead). Every other stem
    /// always plays with exactly the same gain as its siblings (same arrangement fade, bus volume and duck filter,
    /// started on the same DSP sample), so the playback layout pre-mixes them into one <see cref="MusicStem.Base"/>
    /// clip. The mixer output is the same (to float rounding) while clip memory and voice count drop: each 20 s
    /// stereo stem is 6.7 MiB of float PCM at 44.1 kHz.
    /// <para>
    /// The Unity MusicPlayer gives a stem its own gain curve only when <see cref="IsFaded"/> is true, so a new
    /// independently faded layer must be added here (which also keeps it out of the Base mix).
    /// </para>
    /// </summary>
    public static class MusicLayers
    {
        /// <summary>True if the game fades <paramref name="stem"/> of <paramref name="mode"/> independently.</summary>
        public static bool IsFaded(MusicMode mode, MusicStem stem)
        {
            return mode == MusicMode.Run && (stem == MusicStem.Arp || stem == MusicStem.Lead);
        }
    }
}

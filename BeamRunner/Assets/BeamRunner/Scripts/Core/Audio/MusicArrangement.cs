using System;
using System.Collections.Generic;

namespace BeamRunner.Core.Audio
{
    /// <summary>
    /// A rendered music arrangement: several stems of identical length that loop seamlessly and are meant to
    /// be started on the same DSP sample, plus the timing needed to derive the beat phase. In the playback layout the
    /// always-on layers are one <see cref="MusicStem.Base"/> stem and only independently faded layers are separate.
    /// </summary>
    public sealed class MusicArrangement
    {
        private readonly MusicStem[] _stemIds;
        private readonly RenderedAudio[] _stems;

        public MusicArrangement(MusicMode mode, int sampleRate, int loopFrames, int beatsPerLoop, int beatsPerBar,
            MusicStem[] stemIds, RenderedAudio[] stems)
        {
            if (stemIds == null || stems == null || stemIds.Length != stems.Length)
            {
                throw new ArgumentException("Stem ids and stems must match.");
            }

            Mode = mode;
            SampleRate = sampleRate;
            LoopFrames = loopFrames;
            BeatsPerLoop = beatsPerLoop;
            BeatsPerBar = beatsPerBar;
            _stemIds = stemIds;
            _stems = stems;
        }

        public MusicMode Mode { get; }

        public int SampleRate { get; }

        /// <summary>Loop length in sample frames (every stem has exactly this many frames).</summary>
        public int LoopFrames { get; }

        /// <summary>Number of beats (quarter notes) in one loop.</summary>
        public int BeatsPerLoop { get; }

        public int BeatsPerBar { get; }

        /// <summary>Exact tempo implied by the loop length (beats per minute).</summary>
        public double Bpm => BeatsPerLoop * 60.0 * SampleRate / LoopFrames;

        public double LoopSeconds => (double)LoopFrames / SampleRate;

        public double SecondsPerBeat => LoopSeconds / BeatsPerLoop;

        public IReadOnlyList<MusicStem> StemIds => _stemIds;

        public IReadOnlyList<RenderedAudio> Stems => _stems;

        public int StemCount => _stems.Length;

        /// <summary>The stem with the given id, or null if this arrangement has no such layer.</summary>
        public RenderedAudio GetStem(MusicStem id)
        {
            for (int i = 0; i < _stemIds.Length; i++)
            {
                if (_stemIds[i] == id)
                {
                    return _stems[i];
                }
            }

            return null;
        }

        /// <summary>Beat phase (0..1 within the current beat) for a playback position in frames.</summary>
        public double BeatPhaseAt(long frame)
        {
            double beats = (double)(frame % LoopFrames) * BeatsPerLoop / LoopFrames;
            return beats - Math.Floor(beats);
        }
    }
}

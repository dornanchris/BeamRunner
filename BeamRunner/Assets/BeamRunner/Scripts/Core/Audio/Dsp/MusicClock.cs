using System;

namespace BeamRunner.Core.Audio
{
    /// <summary>
    /// Maps musical time (beats) to sample frames for one loop. The loop length is rounded to whole frames
    /// and every event is placed at an exact fraction of it, so the effective tempo is
    /// <c>BeatsPerLoop·60·SampleRate / LoopFrames</c>.
    /// </summary>
    public sealed class MusicClock
    {
        public MusicClock(int sampleRate, double bpm, int beatsPerLoop)
        {
            SampleRate = sampleRate;
            BeatsPerLoop = beatsPerLoop;
            LoopFrames = (int)Math.Round(beatsPerLoop * 60.0 / bpm * sampleRate);
            FramesPerBeat = (double)LoopFrames / beatsPerLoop;
        }

        public int SampleRate { get; }

        public int BeatsPerLoop { get; }

        public int LoopFrames { get; }

        public double FramesPerBeat { get; }

        public double SecondsPerBeat => FramesPerBeat / SampleRate;

        /// <summary>Frame index of a beat position (may exceed the loop for tails).</summary>
        public int Frame(double beat)
        {
            return (int)Math.Round(beat * FramesPerBeat);
        }

        /// <summary>Length of <paramref name="beats"/> in frames.</summary>
        public int Frames(double beats)
        {
            return Math.Max(1, (int)Math.Round(beats * FramesPerBeat));
        }

        /// <summary>A rate in Hz that completes exactly <paramref name="cyclesPerLoop"/> cycles per loop.</summary>
        public float LoopRate(int cyclesPerLoop)
        {
            return (float)(cyclesPerLoop * (double)SampleRate / LoopFrames);
        }
    }
}

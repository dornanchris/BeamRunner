using System;

namespace BeamRunner.Core.Audio
{
    /// <summary>
    /// Game-over loop: 70 BPM, four bars of Am9 – Fmaj7 – Dm7 – E7 (harmonic-minor turnaround) as a dark,
    /// slowly breathing pad with a soft sub and a sparse glassy bell melody, all in one stem.
    /// </summary>
    internal sealed class GameOverSong : Song
    {
        private static readonly MusicStem[] StemList = { MusicStem.Pads };

        private static readonly int[][] Chords =
        {
            new[] { 57, 60, 64, 71 },
            new[] { 53, 57, 60, 64 },
            new[] { 53, 57, 60, 62 },
            new[] { 52, 56, 59, 62 },
        };

        private static readonly int[] Roots = { 45, 41, 38, 40 };

        // beat, MIDI
        private static readonly double[,] Melody =
        {
            { 0.0, 76 }, { 1.5, 71 }, { 3.0, 72 },
            { 4.0, 69 }, { 6.0, 72 },
            { 8.0, 77 }, { 9.5, 76 }, { 11.0, 74 },
            { 12.0, 76 }, { 14.5, 68 },
        };

        public override MusicMode Mode => MusicMode.GameOver;

        public override double Bpm => 70.0;

        public override int Bars => 4;

        public override MusicStem[] Stems => StemList;

        public override float TailSeconds(MusicStem stem)
        {
            return 6f;
        }

        public override float StemGain(MusicStem stem)
        {
            return 0.489f;
        }

        public override void Render(MusicStem stem, StereoBuffer b, MusicClock c, int seed)
        {
            float sr = c.SampleRate;
            var patch = new PadPatch
            {
                Voices = 3,
                Detune = 10f,
                Width = 0.9f,
                Cutoff = 1400f,
                Q = 0.75f,
                EnvOctaves = 1.3f,
                Attack = 1.1f,
                Decay = 3f,
                Sustain = 0.85f,
                Release = 2.2f,
                LfoOctaves = 0.6f,
            };
            var bass = new SynthPatch
            {
                Sub = 1f,
                SubRatio = 1f,
                Saw = 0.08f,
                Cutoff = 300f,
                EnvOctaves = 0.5f,
                FilterDecay = 1f,
                Q = 0.7f,
                Attack = 0.25f,
                Decay = 4f,
                Sustain = 0.8f,
                Release = 1.2f,
            };
            RenderChordsHalfRate(b, c, Chords, 4, 3.6, 0.3f, patch, 0f, seed, 1);
            var chorus = new Chorus(sr, 15f, 5f, c.LoopRate(Math.Max(1, (int)Math.Round(0.25 * c.LoopFrames / sr))));
            chorus.Process(b.L, b.R, b.L, b.R, 0, b.Frames, 0.5f);
            for (int ch = 0; ch < 4; ch++)
            {
                Voices.Subtractive(b, c.Frame(ch * 4), c.Frames(3.6), Hz(Roots[ch]), 0.2f, 0f, bass, NoteSeed(seed, ch, 20), sr);
            }

            var bells = new StereoBuffer(b.Frames);
            for (int i = 0; i < Melody.GetLength(0); i++)
            {
                float pan = (i & 1) == 0 ? -0.2f : 0.2f;
                Voices.Bell(bells, c.Frame(Melody[i, 0]), Hz((int)Melody[i, 1]), 0.22f, pan, 2.6f, 1f, 0.35f, sr, 0.003f, 0.18f);
            }

            var delay = new PingPongDelay(c.Frames(0.75), 0.4f, 2500f, 300f, sr);
            delay.Process(bells.L, bells.R, bells.L, bells.R, 0, bells.Frames, 0.5f, 1f);
            b.Mix(bells, 1f);

            var hp = Biquad.HighPass(40.0, 0.7, sr);
            var hp2 = Biquad.HighPass(40.0, 0.7, sr);
            hp.ProcessInPlace(b.L, 0, b.Frames);
            hp2.ProcessInPlace(b.R, 0, b.Frames);
            var air = Biquad.HighShelf(4000.0, 3.0, sr);
            var air2 = Biquad.HighShelf(4000.0, 3.0, sr);
            air.ProcessInPlace(b.L, 0, b.Frames);
            air2.ProcessInPlace(b.R, 0, b.Frames);
            var hall = new Reverb(sr, 1.6f, 5f, 4000f, 35f);
            hall.Process(b.L, b.R, b.L, b.R, 0, b.Frames, 0.7f, 1f);
        }
    }
}

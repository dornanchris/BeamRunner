using System;

namespace BeamRunner.Core.Audio
{
    /// <summary>
    /// Menu arrangement: 96 BPM, dreamy Fmaj7 – G6 – Am7 – Em7 (VI–VII–i–v in A minor, E4 as a common tone),
    /// slow-swelling pads, sparse FM-bell arp through a long ping-pong, soft sine bass and a light half-time kit.
    /// </summary>
    internal sealed class MenuSong : Song
    {
        private static readonly MusicStem[] StemList =
        {
            MusicStem.Drums, MusicStem.Bass, MusicStem.Pads, MusicStem.Arp,
        };

        private static readonly int[][] Chords =
        {
            new[] { 53, 57, 60, 64 },
            new[] { 55, 59, 62, 64 },
            new[] { 57, 60, 64, 67 },
            new[] { 55, 59, 62, 64 },
        };

        private static readonly int[] BassRoots = { 41, 43, 45, 40 };

        private static readonly int[][] ArpTones =
        {
            new[] { 65, 69, 72, 76, 81 },
            new[] { 67, 71, 74, 76, 79 },
            new[] { 69, 72, 76, 79, 81 },
            new[] { 67, 71, 74, 76, 79 },
        };

        // Eighth-note patterns (−1 = rest) for the first and second bar of each chord.
        private static readonly int[] ArpA = { 0, 2, 4, 3, -1, 2, 4, 1 };
        private static readonly int[] ArpB = { 0, 2, 4, -1, 3, 2, 1, -1 };

        private static readonly double[] KickPattern = { 0.0, 2.5 };

        public override MusicMode Mode => MusicMode.Menu;

        public override double Bpm => 96.0;

        public override int Bars => 8;

        public override MusicStem[] Stems => StemList;

        public override float TailSeconds(MusicStem stem)
        {
            switch (stem)
            {
                case MusicStem.Bass:
                    return 1.2f;
                case MusicStem.Drums:
                    return 3.2f;
                default:
                    return 5.5f;
            }
        }

        public override float StemGain(MusicStem stem)
        {
            switch (stem)
            {
                case MusicStem.Drums:
                    return 0.355f;
                case MusicStem.Bass:
                    return 0.158f;
                case MusicStem.Pads:
                    return 0.446f;
                case MusicStem.Arp:
                    return 0.600f;
                default:
                    return 0.5f;
            }
        }

        public override void Render(MusicStem stem, StereoBuffer buffer, MusicClock clock, int seed)
        {
            switch (stem)
            {
                case MusicStem.Drums:
                    RenderDrums(buffer, clock, seed);
                    break;
                case MusicStem.Bass:
                    RenderBass(buffer, clock, seed);
                    break;
                case MusicStem.Pads:
                    RenderPads(buffer, clock, seed);
                    break;
                case MusicStem.Arp:
                    RenderArp(buffer, clock, seed);
                    break;
            }
        }

        private double[] KickBeats()
        {
            var k = new double[Bars * KickPattern.Length];
            for (int bar = 0; bar < Bars; bar++)
            {
                for (int i = 0; i < KickPattern.Length; i++)
                {
                    k[bar * KickPattern.Length + i] = bar * 4 + KickPattern[i];
                }
            }

            return k;
        }

        private void RenderDrums(StereoBuffer b, MusicClock c, int seed)
        {
            float sr = c.SampleRate;
            var rng = new Rng(seed);
            var dry = new StereoBuffer(b.Frames);
            var wet = new StereoBuffer(b.Frames);
            double[] kicks = KickBeats();
            for (int i = 0; i < kicks.Length; i++)
            {
                Voices.Kick(dry, c.Frame(kicks[i]), (i & 1) == 0 ? 0.8f : 0.6f, sr, 0.95f, 0.45f);
            }

            for (int bar = 0; bar < Bars; bar++)
            {
                double b0 = bar * 4;

                // Soft half-time snare with a long reverb send.
                Voices.Snare(wet, c.Frame(b0 + 2), 0.3f, sr, NoteSeed(seed, bar, 2), 0.12f, 0.05f);
                for (int e = 0; e < 8; e++)
                {
                    float v = (e & 1) == 1 ? 1f : 0.6f;
                    float human = 1f + rng.Range(-0.1f, 0.1f);
                    Voices.Hat(dry, c.Frame(b0 + e * 0.5 + ((e & 1) == 1 ? 0.03 : 0.0)), 0.22f * v * human, 0.06f, 0.25f, sr,
                        NoteSeed(seed, bar, 10 + e));
                }

                for (int s = 0; s < 16; s++)
                {
                    float v = (s & 1) == 1 ? 0.8f : 0.45f;
                    Voices.Hat(dry, c.Frame(b0 + s * 0.25 + 0.012), 0.1f * v, 0.035f, -0.35f, sr, NoteSeed(seed, bar, 40 + s), 0.1f, 9000f);
                }
            }

            ReverseSwell(dry, c.Frame((Bars - 1) * 4 + 2), c.Frame(Bars * 4), 0.1f, sr, (uint)seed);

            b.Mix(dry, 1f);
            b.Mix(wet, 1f);

            // One room for everything: the snare gets a big send, the rest of the kit a little.
            wet.Mix(dry, 0.2f);
            var room = new Reverb(sr, 1.3f, 2.6f, 5000f, 18f);
            room.Process(wet.L, wet.R, b.L, b.R, 0, b.Frames, 0.7f, 1f);
        }

        /// <summary>Reverse-cymbal style noise swell that rises into <paramref name="endFrame"/> and stops dead.</summary>
        private static void ReverseSwell(StereoBuffer b, int startFrame, int endFrame, float gain, float sr, uint seed)
        {
            var nl = new Noise(seed ^ 0x1234u);
            var nr = new Noise(seed ^ 0x9876u);
            var fl = new Svf();
            var fr = new Svf();
            int len = Math.Max(1, endFrame - startFrame);
            int fade = Math.Max(1, (int)(0.006f * sr));
            for (int i = 0; i < len && startFrame + i < b.Frames; i++)
            {
                float t = (float)i / len;
                if ((i & 15) == 0)
                {
                    float cut = 1200f * Voices.Exp2(3.2f * t);
                    fl.Set(cut, 0.9f, sr);
                    fr.Set(cut * 1.05f, 0.9f, sr);
                }

                float env = t * t * t;
                int left = len - i;
                if (left < fade)
                {
                    env *= (float)left / fade;
                }

                fl.Process(nl.White());
                fr.Process(nr.White());
                b.L[startFrame + i] += (fl.High * 0.6f + fl.Band * 0.4f) * env * gain;
                b.R[startFrame + i] += (fr.High * 0.6f + fr.Band * 0.4f) * env * gain;
            }
        }

        private void RenderBass(StereoBuffer b, MusicClock c, int seed)
        {
            float sr = c.SampleRate;
            var patch = new SynthPatch
            {
                Saw = 0.18f,
                Sub = 1f,
                SubRatio = 1f,
                Detune = 4f,
                Cutoff = 420f,
                KeyTrack = 0.3f,
                EnvOctaves = 0.8f,
                FilterDecay = 0.5f,
                Q = 0.8f,
                Attack = 0.018f,
                Decay = 2.5f,
                Sustain = 0.7f,
                Release = 0.35f,
                Drive = 0.9f,
            };
            // Rendered at half rate (soft, band-limited content) and upsampled.
            var half = new StereoBuffer(b.Frames / 2 + 2);
            float hsr = sr * 0.5f;
            for (int bar = 0; bar < Bars; bar++)
            {
                int root = BassRoots[bar / 2];
                double b0 = bar * 4;
                Voices.Subtractive(half, c.Frame(b0) / 2, c.Frames(2.2) / 2, Hz(root), 1f, 0f, patch, NoteSeed(seed, bar, 0), hsr);
                Voices.Subtractive(half, c.Frame(b0 + 2.5) / 2, c.Frames(0.8) / 2, Hz(root), 0.85f, 0f, patch, NoteSeed(seed, bar, 1), hsr);
                Voices.Subtractive(half, c.Frame(b0 + 3.5) / 2, c.Frames(0.35) / 2, Hz(root + 12), 0.5f, 0f, patch, NoteSeed(seed, bar, 2), hsr);
            }

            Voices.Upsample2x(half, b);

            b.Multiply(Sidechain(c, KickBeats(), 0.35f, 0.9, b.Frames));
        }

        private void RenderPads(StereoBuffer b, MusicClock c, int seed)
        {
            float sr = c.SampleRate;
            var patch = new PadPatch
            {
                Voices = 3,
                Detune = 11f,
                Width = 0.9f,
                Cutoff = 1900f,
                Q = 0.8f,
                EnvOctaves = 1.2f,
                Attack = 0.9f,
                Decay = 2.5f,
                Sustain = 0.85f,
                Release = 1.8f,
                LfoOctaves = 0.7f,
            };
            RenderChordsHalfRate(b, c, Chords, 8, 7.5, 0.3f, patch, 0.35f, seed, 1);
            var chorus = new Chorus(sr, 15f, 5f, c.LoopRate(Math.Max(1, (int)Math.Round(0.3 * c.LoopFrames / sr))));
            chorus.Process(b.L, b.R, b.L, b.R, 0, b.Frames, 0.5f);

            var hp = Biquad.HighPass(150.0, 0.7, sr);
            var hp2 = Biquad.HighPass(150.0, 0.7, sr);
            hp.ProcessInPlace(b.L, 0, b.Frames);
            hp2.ProcessInPlace(b.R, 0, b.Frames);
            var air = Biquad.HighShelf(5000.0, 3.0, sr);
            var air2 = Biquad.HighShelf(5000.0, 3.0, sr);
            air.ProcessInPlace(b.L, 0, b.Frames);
            air2.ProcessInPlace(b.R, 0, b.Frames);
            var hall = new Reverb(sr, 1.5f, 4.0f, 4500f, 30f);
            hall.Process(b.L, b.R, b.L, b.R, 0, b.Frames, 0.65f, 1f);
            b.Multiply(Sidechain(c, KickBeats(), 0.28f, 1.0, b.Frames));
        }

        private void RenderArp(StereoBuffer b, MusicClock c, int seed)
        {
            float sr = c.SampleRate;
            for (int bar = 0; bar < Bars; bar++)
            {
                int[] tones = ArpTones[bar / 2];
                int[] pat = (bar & 1) == 0 ? ArpA : ArpB;
                for (int e = 0; e < 8; e++)
                {
                    int idx = pat[e];
                    if (idx < 0)
                    {
                        continue;
                    }

                    float vel = e == 0 ? 1f : ((e & 1) == 0 ? 0.8f : 0.65f);
                    float pan = (e & 1) == 0 ? -0.25f : 0.25f;
                    Voices.Bell(b, c.Frame(bar * 4 + e * 0.5), Hz(tones[idx]), 0.3f * vel, pan, 1.3f, 2f, 0.42f, sr, 0.002f, 0.12f);
                }
            }

            var delay = new PingPongDelay(c.Frames(0.75), 0.48f, 2800f, 300f, sr);
            delay.Process(b.L, b.R, b.L, b.R, 0, b.Frames, 0.6f, 1f);
            var hall = new Reverb(sr, 1.4f, 3.6f, 5000f, 25f);
            hall.Process(b.L, b.R, b.L, b.R, 0, b.Frames, 0.5f, 1f);
            b.Multiply(Sidechain(c, KickBeats(), 0.2f, 0.8, b.Frames));
        }
    }
}

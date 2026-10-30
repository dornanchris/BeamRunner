using System;

namespace BeamRunner.Core.Audio
{
    /// <summary>
    /// In-run arrangement: 116 BPM, A minor, 8 bars of Am – F – C – G (two bars each, voice-led pads),
    /// four-on-the-floor with gated snare, pumping octave bass, plucky 16th arp through a dotted-eighth
    /// ping-pong, and a dotted-rhythm hook for high intensity / Overdrive.
    /// </summary>
    internal sealed class RunSong : Song
    {
        private static readonly MusicStem[] StemList =
        {
            MusicStem.Drums, MusicStem.Bass, MusicStem.Pads, MusicStem.Arp, MusicStem.Lead,
        };

        // Voice-led pad chords (MIDI): Am, F/A, C/G, G.
        private static readonly int[][] Chords =
        {
            new[] { 57, 60, 64, 69 },
            new[] { 57, 60, 65, 69 },
            new[] { 55, 60, 64, 67 },
            new[] { 55, 59, 62, 67 },
        };

        private static readonly int[] BassRoots = { 33, 29, 36, 31 };

        // Arp chord tones (root position around A4–C6).
        private static readonly int[][] ArpTones =
        {
            new[] { 69, 72, 76, 81, 84 },
            new[] { 69, 72, 77, 81, 84 },
            new[] { 67, 72, 76, 79, 84 },
            new[] { 67, 71, 74, 79, 83 },
        };

        private static readonly int[] ArpPattern = { 0, 1, 2, 3, 4, 3, 2, 1 };

        // Lead hook: bar, 16th step, length in 16ths, MIDI. Tresillo (3+3+2) call, stepwise answer.
        private static readonly int[,] Hook =
        {
            { 0, 0, 3, 76 }, { 0, 3, 3, 81 }, { 0, 6, 2, 79 }, { 0, 8, 6, 76 }, { 0, 14, 2, 72 },
            { 1, 0, 4, 74 }, { 1, 4, 2, 72 }, { 1, 6, 9, 69 },
            { 2, 0, 3, 77 }, { 2, 3, 3, 81 }, { 2, 6, 2, 79 }, { 2, 8, 6, 77 }, { 2, 14, 2, 72 },
            { 3, 0, 4, 74 }, { 3, 4, 2, 72 }, { 3, 6, 9, 69 },
            { 4, 0, 3, 76 }, { 4, 3, 3, 79 }, { 4, 6, 2, 84 }, { 4, 8, 6, 83 }, { 4, 14, 2, 79 },
            { 5, 0, 4, 76 }, { 5, 4, 2, 74 }, { 5, 6, 9, 72 },
            { 6, 0, 3, 74 }, { 6, 3, 3, 79 }, { 6, 6, 2, 83 }, { 6, 8, 6, 81 }, { 6, 14, 2, 79 },
            { 7, 0, 4, 74 }, { 7, 4, 2, 76 }, { 7, 6, 6, 79 }, { 7, 12, 2, 76 }, { 7, 14, 2, 74 },
        };

        public override MusicMode Mode => MusicMode.Run;

        public override double Bpm => 116.0;

        public override int Bars => 8;

        public override MusicStem[] Stems => StemList;

        public override float TailSeconds(MusicStem stem)
        {
            switch (stem)
            {
                case MusicStem.Bass:
                    return 0.6f;
                case MusicStem.Drums:
                    return 2.6f;
                default:
                    return 4.5f;
            }
        }

        public override float StemGain(MusicStem stem)
        {
            switch (stem)
            {
                case MusicStem.Drums:
                    return 0.329f;
                case MusicStem.Bass:
                    return 0.320f;
                case MusicStem.Pads:
                    return 0.388f;
                case MusicStem.Arp:
                    return 0.250f;
                case MusicStem.Lead:
                    return 0.176f;
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
                case MusicStem.Lead:
                    RenderLead(buffer, clock, seed);
                    break;
            }
        }

        private static double[] KickBeats(int bars)
        {
            var k = new double[bars * 4];
            for (int i = 0; i < k.Length; i++)
            {
                k[i] = i;
            }

            return k;
        }

        private void RenderDrums(StereoBuffer b, MusicClock c, int seed)
        {
            float sr = c.SampleRate;
            var rng = new Rng(seed);
            float[] hatVel = { 0.62f, 0.34f, 0.9f, 0.4f };
            for (int bar = 0; bar < Bars; bar++)
            {
                double b0 = bar * 4;
                for (int beat = 0; beat < 4; beat++)
                {
                    Voices.Kick(b, c.Frame(b0 + beat), 0.95f, sr);
                }

                Voices.Snare(b, c.Frame(b0 + 1), 0.7f, sr, NoteSeed(seed, bar, 1));
                if (bar == Bars - 1)
                {
                    // Snare roll into the loop point.
                    Voices.Snare(b, c.Frame(b0 + 3.0), 0.26f, sr, NoteSeed(seed, bar, 30), 0.09f, 0.03f);
                    Voices.Snare(b, c.Frame(b0 + 3.25), 0.32f, sr, NoteSeed(seed, bar, 31), 0.09f, 0.03f);
                    Voices.Snare(b, c.Frame(b0 + 3.5), 0.4f, sr, NoteSeed(seed, bar, 32), 0.09f, 0.03f);
                    Voices.Snare(b, c.Frame(b0 + 3.75), 0.5f, sr, NoteSeed(seed, bar, 33), 0.1f, 0.04f);
                }
                else
                {
                    Voices.Snare(b, c.Frame(b0 + 3), 0.7f, sr, NoteSeed(seed, bar, 3));
                    if (bar == 3)
                    {
                        Voices.Snare(b, c.Frame(b0 + 3.75), 0.2f, sr, NoteSeed(seed, bar, 4), 0.07f, 0.03f);
                    }
                }

                bool openBars = (bar & 3) >= 2;
                for (int step = 0; step < 16; step++)
                {
                    double beat = b0 + step * 0.25 + ((step & 1) == 1 ? 0.02 : 0.0);
                    uint hs = NoteSeed(seed, bar, 100 + step);
                    float human = 1f + rng.Range(-0.08f, 0.08f);
                    if (openBars && (step & 3) == 2)
                    {
                        Voices.Hat(b, c.Frame(beat), 0.27f * human, 0.3f, -0.2f, sr, hs, 0.45f, 6500f);
                    }
                    else
                    {
                        float v = hatVel[step & 3] * human;
                        float decay = (step & 3) == 2 ? 0.07f : 0.045f;
                        Voices.Hat(b, c.Frame(beat), 0.3f * v, decay, 0.22f, sr, hs);
                    }
                }
            }

            Voices.Crash(b, 0, 0.2f, 2.2f, sr, NoteSeed(seed, 99, 99));

            // Drum-bus glue: shave the kick+snare coincidences on 2 and 4 by ~2.5 dB.
            b.Saturate(0.75f, 0.55f);

            var room = new Reverb(sr, 0.75f, 1.1f, 6500f, 8f);
            room.Process(b.L, b.R, b.L, b.R, 0, b.Frames, 0.2f, 1f);
        }

        private void RenderBass(StereoBuffer b, MusicClock c, int seed)
        {
            float sr = c.SampleRate;
            var patch = new SynthPatch
            {
                Saw = 0.55f,
                Saw2 = 0.55f,
                Sub = 0.65f,
                SubRatio = 1f,
                Detune = 7f,
                Cutoff = 330f,
                KeyTrack = 0.5f,
                EnvOctaves = 2.3f,
                FilterDecay = 0.24f,
                Q = 1.25f,
                Attack = 0.003f,
                Decay = 0.4f,
                Sustain = 0.75f,
                Release = 0.05f,
                Drive = 1.5f,
            };
            var high = patch;
            high.SubRatio = 0.5f;
            high.Sub = 0.55f;
            // Rendered at half rate (content is band-limited well below 11 kHz) and upsampled.
            var half = new StereoBuffer(b.Frames / 2 + 2);
            float hsr = sr * 0.5f;
            int gate = c.Frames(0.5 * 0.62) / 2;
            for (int bar = 0; bar < Bars; bar++)
            {
                int root = BassRoots[bar / 2];
                for (int e = 0; e < 8; e++)
                {
                    bool up = (e & 1) == 1;
                    int midi = up ? root + 12 : root;
                    Voices.Subtractive(half, c.Frame(bar * 4 + e * 0.5) / 2, gate, Hz(midi), up ? 0.8f : 1f, 0f,
                        up ? high : patch, NoteSeed(seed, bar, e), hsr);
                }
            }

            Voices.Upsample2x(half, b);

            b.Multiply(Sidechain(c, KickBeats(Bars), 0.62f, 0.72, b.Frames));
        }

        private void RenderPads(StereoBuffer b, MusicClock c, int seed)
        {
            float sr = c.SampleRate;
            var patch = new PadPatch
            {
                Voices = 3,
                Detune = 13f,
                Width = 0.85f,
                Cutoff = 2300f,
                Q = 0.9f,
                EnvOctaves = 1.0f,
                Attack = 0.35f,
                Decay = 1.6f,
                Sustain = 0.85f,
                Release = 1.1f,
                LfoOctaves = 0.55f,
            };
            RenderChordsHalfRate(b, c, Chords, 8, 7.75, 0.3f, patch, 0.4f, seed, 2);
            var chorus = new Chorus(sr, 13f, 4f, c.LoopRate(Math.Max(1, (int)Math.Round(0.4 * c.LoopFrames / sr))));
            chorus.Process(b.L, b.R, b.L, b.R, 0, b.Frames, 0.45f);

            var hp = Biquad.HighPass(170.0, 0.7, sr);
            var hp2 = Biquad.HighPass(170.0, 0.7, sr);
            hp.ProcessInPlace(b.L, 0, b.Frames);
            hp2.ProcessInPlace(b.R, 0, b.Frames);
            var air = Biquad.HighShelf(5000.0, 3.0, sr);
            var air2 = Biquad.HighShelf(5000.0, 3.0, sr);
            air.ProcessInPlace(b.L, 0, b.Frames);
            air2.ProcessInPlace(b.R, 0, b.Frames);
            var hall = new Reverb(sr, 1.35f, 3.2f, 6000f, 22f);
            hall.Process(b.L, b.R, b.L, b.R, 0, b.Frames, 0.55f, 1f);
            b.Multiply(Sidechain(c, KickBeats(Bars), 0.5f, 0.8, b.Frames));
        }

        private void RenderArp(StereoBuffer b, MusicClock c, int seed)
        {
            float sr = c.SampleRate;
            var patch = new SynthPatch
            {
                Saw = 0.7f,
                Pulse = 0.45f,
                PulseWidth = 0.3f,
                Detune = 6f,
                Cutoff = 800f,
                KeyTrack = 0.3f,
                EnvOctaves = 3.0f,
                FilterDecay = 0.17f,
                Q = 2.0f,
                Attack = 0.001f,
                Decay = 0.32f,
                Sustain = 0f,
                Release = 0.08f,
                Drive = 0.8f,
            };
            int gate = c.Frames(0.25 * 0.5);
            for (int bar = 0; bar < Bars; bar++)
            {
                int[] tones = ArpTones[bar / 2];
                for (int step = 0; step < 16; step++)
                {
                    double beat = bar * 4 + step * 0.25;
                    int f = c.Frame(beat);

                    // Filter opens and closes once per loop (loop-periodic ⇒ seamless).
                    var p = patch;
                    p.Cutoff = patch.Cutoff * Voices.Exp2(0.75f * DspMath.Sin01(DspMath.Wrap01((float)f / c.LoopFrames + 0.75f)));
                    float vel = (step & 3) == 0 ? 1f : ((step & 1) == 0 ? 0.82f : 0.62f);
                    float pan = (step & 1) == 0 ? -0.15f : 0.15f;
                    Voices.Subtractive(b, f, gate, Hz(tones[ArpPattern[step & 7]]), vel, pan, p, NoteSeed(seed, bar, step), sr);
                }
            }

            var delay = new PingPongDelay(c.Frames(0.75), 0.42f, 3200f, 280f, sr);
            delay.Process(b.L, b.R, b.L, b.R, 0, b.Frames, 0.55f, 1f);
            var hall = new Reverb(sr, 1.1f, 2.4f, 6000f, 12f);
            hall.Process(b.L, b.R, b.L, b.R, 0, b.Frames, 0.3f, 1f);
            b.Multiply(Sidechain(c, KickBeats(Bars), 0.45f, 0.6, b.Frames));
        }

        private void RenderLead(StereoBuffer b, MusicClock c, int seed)
        {
            float sr = c.SampleRate;
            int count = Hook.GetLength(0);
            var notes = new NoteEvent[count];
            for (int i = 0; i < count; i++)
            {
                double beat = Hook[i, 0] * 4 + Hook[i, 1] * 0.25;
                double len = Hook[i, 2] * 0.25;

                // Short notes are articulated; long notes slur into the next one.
                double gate = Hook[i, 2] >= 6 ? len : len * 0.82;
                float vel = Hook[i, 1] == 0 || Hook[i, 1] == 8 ? 1f : 0.88f;
                notes[i] = new NoteEvent(beat, gate, Hook[i, 3], vel);
            }

            var patch = new LeadPatch
            {
                Detune = 9f,
                SubSquare = 0.35f,
                Cutoff = 2200f,
                EnvOctaves = 1.4f,
                FilterDecay = 0.4f,
                Q = 1.3f,
                Attack = 0.006f,
                Decay = 0.5f,
                Sustain = 0.78f,
                Release = 0.16f,
                Glide = 0.03f,
                VibratoCents = 16f,
                VibratoRate = 5.4f,
                VibratoDelay = 0.2f,
                Drive = 1.2f,
            };
            Voices.MonoLead(b, notes, c, 0.5f, patch, sr);

            int cycles = Math.Max(1, (int)Math.Round(0.55 * c.LoopFrames / sr));
            var chorus = new Chorus(sr, 11f, 3.5f, c.LoopRate(cycles));
            chorus.Process(b.L, b.R, b.L, b.R, 0, b.Frames, 0.55f);
            var delay = new PingPongDelay(c.Frames(0.75), 0.33f, 3500f, 350f, sr);
            delay.Process(b.L, b.R, b.L, b.R, 0, b.Frames, 0.32f, 1f);
            var hall = new Reverb(sr, 1.4f, 2.8f, 5500f, 20f);
            hall.Process(b.L, b.R, b.L, b.R, 0, b.Frames, 0.35f, 1f);
            b.Multiply(Sidechain(c, KickBeats(Bars), 0.2f, 0.5, b.Frames));
        }
    }
}

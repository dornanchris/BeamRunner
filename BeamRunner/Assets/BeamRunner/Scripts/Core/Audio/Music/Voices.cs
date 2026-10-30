using System;
using Unity.IL2CPP.CompilerServices;

namespace BeamRunner.Core.Audio
{
    /// <summary>Subtractive-synth patch for <see cref="Voices.Subtractive"/>.</summary>
    internal struct SynthPatch
    {
        public float Saw;
        public float Saw2;
        public float Pulse;
        public float PulseWidth;
        public float Sub;
        public float SubRatio;
        public float Detune;
        public float Cutoff;
        public float KeyTrack;
        public float EnvOctaves;
        public float FilterDecay;
        public float Q;
        public float Attack;
        public float Decay;
        public float Sustain;
        public float Release;
        public float Drive;
    }

    /// <summary>Stereo unison pad patch for <see cref="Voices.PadChord"/>.</summary>
    internal struct PadPatch
    {
        public int Voices;
        public float Detune;
        public float Width;
        public float Cutoff;
        public float Q;
        public float EnvOctaves;
        public float Attack;
        public float Decay;
        public float Sustain;
        public float Release;
        /// <summary>LFO cycles per frame (use cyclesPerLoop / loopFrames so it stays loop-periodic).</summary>
        public float LfoRate;
        public float LfoOctaves;
    }

    /// <summary>Monophonic lead patch for <see cref="Voices.MonoLead"/>.</summary>
    internal struct LeadPatch
    {
        public float Detune;
        public float SubSquare;
        public float Cutoff;
        public float EnvOctaves;
        public float FilterDecay;
        public float Q;
        public float Attack;
        public float Decay;
        public float Sustain;
        public float Release;
        public float Glide;
        public float VibratoCents;
        public float VibratoRate;
        public float VibratoDelay;
        public float Drive;
    }

    /// <summary>A note in musical time.</summary>
    internal struct NoteEvent
    {
        public double Beat;
        public double Length;
        public int Midi;
        public float Velocity;

        public NoteEvent(double beat, double length, int midi, float velocity)
        {
            Beat = beat;
            Length = length;
            Midi = midi;
            Velocity = velocity;
        }
    }

    /// <summary>
    /// Instrument voices shared by the music arrangements and some SFX. Every voice adds itself into a
    /// <see cref="StereoBuffer"/> starting at a frame and stops at the buffer end, so notes that ring past the
    /// loop land in the tail region and are folded back by the renderer.
    /// </summary>
    [Il2CppSetOption(Option.NullChecks, false)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
    internal static class Voices
    {
        private static readonly float[] MetalHz = { 205.3f, 304.4f, 369.6f, 522.7f, 540.0f, 800.0f };

        // ------------------------------------------------------------------ drums

        /// <summary>Punchy synth kick: sine with a two-stage pitch drop, noise click, soft saturation.</summary>
        public static void Kick(StereoBuffer b, int start, float gain, float sr, float tone = 1f, float length = 0.42f)
        {
            int len = (int)(length * sr);
            int end = Math.Min(b.Frames, start + len);
            float[] l = b.L;
            float[] r = b.R;
            float inv = 1f / sr;
            float phase = 0f;
            float pe1 = 1f;
            float pc1 = (float)Math.Exp(-1.0 / (0.034 * sr));
            float pe2 = 1f;
            float pc2 = (float)Math.Exp(-1.0 / (0.0045 * sr));
            float amp = 1f;
            float ac = DspMath.DecayCoef(length * 1.05f, sr);
            int hold = (int)(0.03f * sr);
            int attack = Math.Max(1, (int)(0.0008f * sr));
            int fade = Math.Max(1, (int)(0.012f * sr));
            var noise = new Noise((uint)start + 0x4B1Du);
            OnePole hp = OnePole.FromHz(3000f, sr);
            float ce = 1f;
            float cc = (float)Math.Exp(-1.0 / (0.0018 * sr));
            const float Drive = 1.7f;
            float norm = gain / DspMath.Tanh(Drive);
            float baseHz = 49f * tone;
            for (int i = 0, j = start; j < end; i++, j++)
            {
                float f = baseHz + 72f * tone * pe1 + 170f * pe2;
                pe1 *= pc1;
                pe2 = pe2 > 1e-9f ? pe2 * pc2 : 0f;
                phase += f * inv;
                if (phase >= 1f)
                {
                    phase -= 1f;
                }

                float a = i < attack ? (float)i / attack : 1f;
                if (i > hold)
                {
                    amp *= ac;
                }

                float click = ce > 0f ? hp.HighPass(noise.White()) * ce * 0.28f : 0f;
                ce = ce > 1e-9f ? ce * cc : 0f;
                float s = DspMath.Tanh((DspMath.Sin01(phase) * amp + click) * a * Drive) * norm;
                int left = len - i;
                if (left < fade)
                {
                    s *= (float)left / fade;
                }

                l[j] += s;
                r[j] += s;
            }
        }

        /// <summary>
        /// 80s gated snare: tuned body + clap bursts + wide decorrelated noise that holds, then gets cut off.
        /// </summary>
        public static void Snare(StereoBuffer b, int start, float gain, float sr, uint seed, float gateSeconds = 0.17f, float tail = 0.06f)
        {
            int gate = (int)(gateSeconds * sr);
            int rel = Math.Max(1, (int)(tail * sr));
            int len = gate + rel;
            int end = Math.Min(b.Frames, start + len);
            float[] l = b.L;
            float[] r = b.R;
            float inv = 1f / sr;
            var nl = new Noise(seed);
            var nr = new Noise(seed ^ 0x5BD1E995u);
            var fl = new Svf();
            var fr = new Svf();
            fl.Set(2200f, 0.6f, sr);
            fr.Set(2350f, 0.6f, sr);
            var t1 = new Osc(0f);
            var t2 = new Osc(0.1f);
            float pe = 1f;
            float pc = (float)Math.Exp(-1.0 / (0.012 * sr));
            float te = 1f;
            float tc = DspMath.DecayCoef(0.16f, sr);
            float ne = 1f;
            float nc = (float)Math.Exp(-1.0 / (0.022 * sr));
            float plateau = 1f;
            float pcoef = DspMath.DecayCoef(1.4f, sr);
            int attack = Math.Max(1, (int)(0.0006f * sr));
            int burst = (int)(0.0095f * sr);
            float burstTau = 0.0028f * sr;
            for (int i = 0, j = start; j < end; i++, j++)
            {
                float f = 1f + 0.35f * pe;
                pe *= pc;
                float tone = (t1.Sine(182f * f * inv) * 0.75f + t2.Sine(329f * f * inv) * 0.3f) * te;
                te *= tc;

                float env = 0.36f * plateau + 0.64f * ne;
                ne *= nc;
                plateau *= pcoef;
                if (i < 3 * burst)
                {
                    int k = i % burst;
                    env += 0.6f * (float)Math.Exp(-k / burstTau);
                }

                float g = i < attack ? (float)i / attack : 1f;
                if (i >= gate)
                {
                    g *= 0.5f + 0.5f * DspMath.Cos01((float)(i - gate) / rel * 0.5f);
                }

                fl.Process(nl.White());
                fr.Process(nr.White());
                float sl = (fl.Band * 1.1f + fl.High * 0.75f) * env;
                float sr2 = (fr.Band * 1.1f + fr.High * 0.75f) * env;
                l[j] += (tone * 0.85f + sl) * g * gain;
                r[j] += (tone * 0.85f + sr2) * g * gain;
            }
        }

        /// <summary>Hi-hat / shaker: 808-style metallic square cluster + noise, high-passed.</summary>
        public static void Hat(StereoBuffer b, int start, float gain, float decay, float pan, float sr, uint seed, float metal = 0.4f, float hpHz = 7200f)
        {
            int len = (int)((decay + 0.005f) * sr);
            int end = Math.Min(b.Frames, start + len);
            float[] l = b.L;
            float[] r = b.R;
            var n = new Noise(seed);
            float inv = 1f / sr;
            float p0 = n.Value(), p1 = n.Value(), p2 = n.Value(), p3 = n.Value(), p4 = n.Value(), p5 = n.Value();
            const float M = 1.72f;
            float i0 = MetalHz[0] * M * inv, i1 = MetalHz[1] * M * inv, i2 = MetalHz[2] * M * inv;
            float i3 = MetalHz[3] * M * inv, i4 = MetalHz[4] * M * inv, i5 = MetalHz[5] * M * inv;
            var hp = new Svf();
            hp.Set(hpHz, 0.8f, sr);
            var bp = new Svf();
            bp.Set(10500f, 1.1f, sr);
            float e = 1f;
            float ec = DspMath.DecayCoef(decay, sr);
            int attack = Math.Max(1, (int)(0.0004f * sr));
            DspMath.Pan(pan, out float gl, out float gr);
            gl *= gain;
            gr *= gain;
            float noiseMix = 1f - metal;
            for (int i = 0, j = start; j < end; i++, j++)
            {
                float m = 0f;
                p0 += i0;
                if (p0 >= 1f)
                {
                    p0 -= 1f;
                }

                p1 += i1;
                if (p1 >= 1f)
                {
                    p1 -= 1f;
                }

                p2 += i2;
                if (p2 >= 1f)
                {
                    p2 -= 1f;
                }

                p3 += i3;
                if (p3 >= 1f)
                {
                    p3 -= 1f;
                }

                p4 += i4;
                if (p4 >= 1f)
                {
                    p4 -= 1f;
                }

                p5 += i5;
                if (p5 >= 1f)
                {
                    p5 -= 1f;
                }

                m += p0 < 0.5f ? 1f : -1f;
                m += p1 < 0.5f ? 1f : -1f;
                m += p2 < 0.5f ? 1f : -1f;
                m += p3 < 0.5f ? 1f : -1f;
                m += p4 < 0.5f ? 1f : -1f;
                m += p5 < 0.5f ? 1f : -1f;
                float x = m * (metal / 6f) + n.White() * noiseMix;
                hp.Process(x);
                float h = hp.High;
                float s = (h * 0.7f + bp.BandPass(h) * 0.6f) * e;
                if (i < attack)
                {
                    s *= (float)i / attack;
                }

                e *= ec;
                l[j] += s * gl;
                r[j] += s * gr;
            }
        }

        /// <summary>Crash cymbal: wide decorrelated noise + metallic cluster, long decay.</summary>
        public static void Crash(StereoBuffer b, int start, float gain, float decay, float sr, uint seed)
        {
            int len = (int)(decay * sr);
            int end = Math.Min(b.Frames, start + len);
            float[] l = b.L;
            float[] r = b.R;
            var nl = new Noise(seed);
            var nr = new Noise(seed * 31u + 7u);
            float inv = 1f / sr;
            var metal = new Osc[6];
            for (int k = 0; k < 6; k++)
            {
                metal[k] = new Osc(nl.Value());
            }

            var hl = new Svf();
            var hr = new Svf();
            hl.Set(3800f, 0.7f, sr);
            hr.Set(4100f, 0.7f, sr);
            float e = 1f;
            float ec = DspMath.DecayCoef(decay, sr);
            float burst = 1f;
            float bc = (float)Math.Exp(-1.0 / (0.05 * sr));
            int attack = Math.Max(1, (int)(0.001f * sr));
            int fade = (int)(0.1f * sr);
            for (int i = 0, j = start; j < end; i++, j++)
            {
                float m = 0f;
                for (int k = 0; k < 6; k++)
                {
                    m += metal[k].Square(MetalHz[k] * 2.9f * inv);
                }

                m *= 0.06f;
                float env = e * (0.7f + 0.6f * burst);
                e *= ec;
                burst *= bc;
                float a = i < attack ? (float)i / attack : 1f;
                int left = len - i;
                if (left < fade)
                {
                    a *= (float)left / fade;
                }

                hl.Process(nl.White() + m);
                hr.Process(nr.White() + m);
                l[j] += hl.High * env * a * gain;
                r[j] += hr.High * env * a * gain;
            }
        }

        // ------------------------------------------------------------------ tonal

        /// <summary>
        /// General subtractive voice: two detuned saws + pulse + sine sub + noise → drive → resonant low-pass
        /// with an exponential filter envelope, ADSR amp. Mono voice, panned.
        /// </summary>
        public static void Subtractive(StereoBuffer b, int start, int gate, float freq, float gain, float pan, in SynthPatch p, uint seed, float sr)
        {
            if (start >= b.Frames || start < 0)
            {
                return;
            }

            var amp = new Adsr(p.Attack, p.Decay, p.Sustain, p.Release, sr);
            amp.Gate(true);
            var noise = new Noise(seed);
            var o1 = new Osc(noise.Value());
            var o2 = new Osc(noise.Value());
            var o3 = new Osc(noise.Value());
            var sub = new Osc(0f);
            float inv = 1f / sr;
            float inc1 = freq * DspMath.Cents(-p.Detune) * inv;
            float inc2 = freq * DspMath.Cents(p.Detune) * inv;
            float inc3 = freq * inv;
            float incSub = freq * (p.SubRatio > 0f ? p.SubRatio : 1f) * inv;
            float pw = p.PulseWidth > 0f ? p.PulseWidth : 0.5f;
            var f = new Svf();
            float fenv = 1f;
            float fcoef = DspMath.DecayCoef(p.FilterDecay > 0f ? p.FilterDecay : 0.2f, sr);
            float baseCut = p.Cutoff * (float)Math.Pow(freq / 261.63f, p.KeyTrack);
            DspMath.Pan(pan, out float gl, out float gr);
            gl *= gain;
            gr *= gain;
            float drive = p.Drive;
            float driveNorm = drive > 0f ? 1f / DspMath.Tanh(drive) : 1f;
            bool useSaw = p.Saw != 0f;
            bool useSaw2 = p.Saw2 != 0f;
            bool usePulse = p.Pulse != 0f;
            bool useSub = p.Sub != 0f;
            int maxLen = gate + (int)((p.Release + 0.01f) * sr);
            int end = Math.Min(b.Frames, start + maxLen);
            float[] l = b.L;
            float[] r = b.R;
            for (int i = 0, j = start; j < end; i++, j++)
            {
                if (i == gate)
                {
                    amp.Gate(false);
                }

                if ((i & 7) == 0)
                {
                    f.Set(baseCut * Exp2(p.EnvOctaves * fenv), p.Q, sr);
                }

                fenv *= fcoef;
                float s = 0f;
                if (useSaw)
                {
                    s += o1.Saw(inc1) * p.Saw;
                }

                if (useSaw2)
                {
                    s += o2.Saw(inc2) * p.Saw2;
                }

                if (usePulse)
                {
                    s += o3.Pulse(inc3, pw) * p.Pulse;
                }

                if (useSub)
                {
                    s += sub.Sine(incSub) * p.Sub;
                }

                if (drive > 0f)
                {
                    s = DspMath.Tanh(s * drive) * driveNorm;
                }

                s = f.LowPass(s) * amp.Next();
                l[j] += s * gl;
                r[j] += s * gr;
                if (i > gate && amp.IsIdle)
                {
                    break;
                }
            }
        }

        /// <summary>
        /// A whole pad chord as one voice: <see cref="PadPatch.Voices"/> detuned saws per note spread across the
        /// stereo field, one shared amp envelope and one resonant low-pass per side (cutoff follows the envelope and a
        /// loop-synced LFO evaluated at absolute frame positions). Much cheaper than per-note voices; designed to be
        /// rendered at half rate and upsampled with <see cref="Upsample2x"/>.
        /// </summary>
        public static void PadChord(StereoBuffer b, int start, int gate, float[] freqs, float[] noteGains, float gain, in PadPatch p, uint seed, float sr)
        {
            if (start >= b.Frames || start < 0 || freqs.Length == 0)
            {
                return;
            }

            int u = Math.Max(1, Math.Min(6, p.Voices));
            int n = freqs.Length * u;
            var noise = new Noise(seed);
            var incs = new float[n];
            var phases = new float[n];
            var gls = new float[n];
            var grs = new float[n];
            float inv = 1f / sr;
            for (int i = 0; i < freqs.Length; i++)
            {
                for (int k = 0; k < u; k++)
                {
                    int v = i * u + k;
                    float spread = u == 1 ? 0f : 2f * k / (u - 1) - 1f;
                    float cents = p.Detune * spread + (noise.Value() - 0.5f) * 3f;
                    incs[v] = Math.Min(0.45f, freqs[i] * DspMath.Cents(cents) * inv);
                    phases[v] = noise.Value();
                    float pan = p.Width * spread * ((i & 1) == 0 ? 1f : -1f);
                    DspMath.Pan(pan, out float gl, out float gr);
                    float g = gain * noteGains[i] / u;
                    gls[v] = gl * g;
                    grs[v] = gr * g;
                }
            }

            var amp = new Adsr(p.Attack, p.Decay, p.Sustain, p.Release, sr);
            amp.Gate(true);
            var fl = new Svf();
            var fr = new Svf();
            int maxLen = gate + (int)((p.Release + 0.02f) * sr);
            int end = Math.Min(b.Frames, start + maxLen);
            float[] l = b.L;
            float[] r = b.R;
            float lfoInc = p.LfoRate;
            for (int i = 0, j = start; j < end; i++, j++)
            {
                if (i == gate)
                {
                    amp.Gate(false);
                }

                float e = amp.Next();
                if ((i & 7) == 0)
                {
                    float lfo = lfoInc > 0f ? DspMath.Sin01(DspMath.Wrap01(j * lfoInc)) : 0f;
                    float cut = p.Cutoff * Exp2(p.LfoOctaves * lfo + p.EnvOctaves * (e - 1f));
                    fl.Set(cut, p.Q, sr);
                    fr.Set(cut * 1.03f, p.Q, sr);
                }

                float sl = 0f;
                float sr2 = 0f;
                for (int v = 0; v < n; v++)
                {
                    float t = phases[v];
                    float dt = incs[v];
                    float x = 2f * t - 1f;
                    if (t < dt)
                    {
                        float q = t / dt;
                        x -= q + q - q * q - 1f;
                    }
                    else if (t > 1f - dt)
                    {
                        float q = (t - 1f) / dt;
                        x -= q * q + q + q + 1f;
                    }

                    t += dt;
                    if (t >= 1f)
                    {
                        t -= 1f;
                    }

                    phases[v] = t;
                    sl += x * gls[v];
                    sr2 += x * grs[v];
                }

                l[j] += fl.LowPass(sl) * e;
                r[j] += fr.LowPass(sr2) * e;
                if (i > gate && amp.IsIdle)
                {
                    break;
                }
            }
        }

        /// <summary>
        /// Adds a half-rate buffer into a full-rate one: even frames copy, odd frames use the 4-point
        /// (−1, 9, 9, −1)/16 midpoint interpolator (good image rejection, no allocation).
        /// </summary>
        public static void Upsample2x(StereoBuffer half, StereoBuffer full)
        {
            float[] hl = half.L;
            float[] hr = half.R;
            float[] fl = full.L;
            float[] fr = full.R;
            int m = half.Frames;
            for (int j = 0; j < full.Frames; j++)
            {
                int k = j >> 1;
                if (k >= m)
                {
                    break;
                }

                if ((j & 1) == 0)
                {
                    fl[j] += hl[k];
                    fr[j] += hr[k];
                }
                else
                {
                    int km = k > 0 ? k - 1 : 0;
                    int k1 = k + 1 < m ? k + 1 : m - 1;
                    int k2 = k + 2 < m ? k + 2 : m - 1;
                    fl[j] += (9f * (hl[k] + hl[k1]) - hl[km] - hl[k2]) * 0.0625f;
                    fr[j] += (9f * (hr[k] + hr[k1]) - hr[km] - hr[k2]) * 0.0625f;
                }
            }
        }

        /// <summary>FM (phase-modulation) bell: bright attack that mellows as the modulation index decays.</summary>
        public static void Bell(StereoBuffer b, int start, float freq, float gain, float pan, float decay, float ratio, float index, float sr, float attack = 0.0015f, float shimmer = 0.25f)
        {
            if (start >= b.Frames || start < 0)
            {
                return;
            }

            int len = (int)((decay + 0.01f) * sr);
            int end = Math.Min(b.Frames, start + len);
            float inv = 1f / sr;
            var c = new Osc(0f);
            var m = new Osc(0f);
            var h = new Osc(0f);
            float incC = freq * inv;
            float incM = freq * ratio * inv;
            float incH = Math.Min(0.45f, freq * 4.07f * inv);
            float e = 1f;
            float ec = DspMath.DecayCoef(decay, sr);
            float me = 1f;
            float mc = DspMath.DecayCoef(decay * 0.3f, sr);
            float he = 1f;
            float hc = DspMath.DecayCoef(decay * 0.12f, sr);
            int att = Math.Max(1, (int)(attack * sr));
            int fade = Math.Max(1, (int)(0.01f * sr));
            DspMath.Pan(pan, out float gl, out float gr);
            gl *= gain;
            gr *= gain;
            float[] l = b.L;
            float[] r = b.R;
            for (int i = 0, j = start; j < end; i++, j++)
            {
                float mod = m.Sine(incM) * index * me;
                float s = c.SinePm(incC, mod) + h.Sine(incH) * shimmer * he;
                s *= e;
                if (i < att)
                {
                    s *= (float)i / att;
                }

                int left = len - i;
                if (left < fade)
                {
                    s *= (float)left / fade;
                }

                e *= ec;
                me = me > 1e-9f ? me * mc : 0f;
                he = he > 1e-9f ? he * hc : 0f;
                l[j] += s * gl;
                r[j] += s * gr;
            }
        }

        /// <summary>
        /// Monophonic lead with glide, delayed vibrato, legato (overlapping/adjacent notes don't retrigger),
        /// two detuned saws + sub square through an enveloped resonant low-pass. Rendered over the whole buffer.
        /// </summary>
        public static void MonoLead(StereoBuffer b, NoteEvent[] notes, MusicClock clock, float gain, in LeadPatch p, float sr)
        {
            if (notes.Length == 0)
            {
                return;
            }

            float inv = 1f / sr;
            var o1 = new Osc(0f);
            var o2 = new Osc(0.37f);
            var o3 = new Osc(0.71f);
            var vib = new Osc(0f);
            var amp = new Adsr(p.Attack, p.Decay, p.Sustain, p.Release, sr);
            var f = new Svf();
            float fenv = 0f;
            float fcoef = DspMath.DecayCoef(p.FilterDecay, sr);
            float glide = DspMath.SmoothCoef(p.Glide, sr);
            float d1 = DspMath.Cents(-p.Detune);
            float d2 = DspMath.Cents(p.Detune);
            float drive = p.Drive;
            float driveNorm = drive > 0f ? 1f / DspMath.Tanh(drive) : 1f;
            float vibDepth = p.VibratoCents * 0.000578f;
            int vibDelay = (int)(p.VibratoDelay * sr);
            int vibRamp = Math.Max(1, (int)(0.35f * sr));
            float[] l = b.L;
            float[] r = b.R;

            int noteIndex = -1;
            int nextStart = clock.Frame(notes[0].Beat);
            int noteOff = int.MaxValue;
            int noteStartFrame = 0;
            float freq = DspMath.MidiToHz(notes[0].Midi);
            float target = freq;
            float vel = notes[0].Velocity;
            for (int j = 0; j < b.Frames; j++)
            {
                if (j == nextStart)
                {
                    noteIndex++;
                    NoteEvent ev = notes[noteIndex];
                    bool legato = noteOff >= j && noteIndex > 0;
                    target = DspMath.MidiToHz(ev.Midi);
                    vel = ev.Velocity;
                    noteOff = clock.Frame(ev.Beat + ev.Length);
                    noteStartFrame = j;
                    if (!legato)
                    {
                        amp.Gate(true);
                        fenv = 1f;
                        if (noteIndex == 0)
                        {
                            freq = target;
                        }
                    }
                    else
                    {
                        fenv = Math.Max(fenv, 0.45f);
                    }

                    nextStart = noteIndex + 1 < notes.Length ? clock.Frame(notes[noteIndex + 1].Beat) : -1;
                }

                if (j == noteOff && nextStart != j)
                {
                    amp.Gate(false);
                }

                freq += (target - freq) * glide;
                int since = j - noteStartFrame;
                float vd = 0f;
                if (since > vibDelay)
                {
                    float ramp = Math.Min(1f, (float)(since - vibDelay) / vibRamp);
                    vd = vibDepth * ramp * vib.Sine(p.VibratoRate * inv);
                }

                float fr = freq * (1f + vd);
                if ((j & 7) == 0)
                {
                    f.Set(p.Cutoff * Exp2(p.EnvOctaves * fenv), p.Q, sr);
                }

                fenv *= fcoef;
                float s = o1.Saw(fr * d1 * inv) + o2.Saw(fr * d2 * inv) + o3.Square(fr * 0.5f * inv) * p.SubSquare;
                if (drive > 0f)
                {
                    s = DspMath.Tanh(s * drive) * driveNorm;
                }

                s = f.LowPass(s) * amp.Next() * vel * gain;
                l[j] += s;
                r[j] += s;
            }
        }

        /// <summary>2^x for modest |x| (≤ ~8).</summary>
        public static float Exp2(float x)
        {
            return (float)Math.Pow(2.0, x);
        }
    }
}

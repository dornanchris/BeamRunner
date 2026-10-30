using System;
using Unity.IL2CPP.CompilerServices;

namespace BeamRunner.Core.Audio
{
    internal enum Wave
    {
        Sine,
        Triangle,
        Square,
        Saw,
        Pulse25,
    }

    internal enum FilterMode
    {
        LowPass,
        BandPass,
        HighPass,
    }

    internal enum EnvShape
    {
        /// <summary>Linear attack, then exponential decay by the given dB over the rest.</summary>
        Perc,

        /// <summary>Cubic rise to full level at the end (reverse-style swell).</summary>
        Swell,

        /// <summary>sin(π·t)^2 hump: rises and falls smoothly.</summary>
        Bump,
    }

    /// <summary>How an SFX is levelled: to a peak, or to a max-momentary loudness under a peak ceiling.</summary>
    internal struct SfxLevel
    {
        public bool ByLoudness;
        public float TargetLufs;
        public float CeilingDb;
        public float MaxPushDb;

        /// <summary>Normalise the peak to <paramref name="db"/> dBFS (short UI ticks, where loudness windows mislead).</summary>
        public static SfxLevel Peak(float db)
        {
            return new SfxLevel { ByLoudness = false, CeilingDb = db };
        }

        /// <summary>Level to a max-momentary loudness, never peaking above <paramref name="ceilingDb"/>.</summary>
        public static SfxLevel Lufs(float target, float ceilingDb = -1f, float maxPushDb = 0f)
        {
            return new SfxLevel { ByLoudness = true, TargetLufs = target, CeilingDb = ceilingDb, MaxPushDb = maxPushDb };
        }
    }

    /// <summary>
    /// Small sound-design toolkit used by <see cref="SfxRenderer"/>: every method adds a layer into one stereo
    /// canvas at a time offset in seconds. <see cref="Finish"/> cleans, trims, normalises and limits.
    /// </summary>
    [Il2CppSetOption(Option.NullChecks, false)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
    internal sealed class SfxKit
    {
        private readonly float _sr;
        private readonly float _inv;

        public SfxKit(int sampleRate, float seconds, int seed)
        {
            _sr = sampleRate;
            _inv = 1f / sampleRate;
            SampleRate = sampleRate;
            Buffer = new StereoBuffer((int)(seconds * sampleRate) + 1);
            Rng = new Rng(seed);
        }

        public int SampleRate { get; }

        public StereoBuffer Buffer { get; }

        public Rng Rng { get; }

        public int Frame(float seconds)
        {
            return (int)(seconds * _sr);
        }

        private uint NextSeed()
        {
            return Rng.NextUInt();
        }

        /// <summary>
        /// Oscillator layer with exponential pitch glide (f0 → f1 over <paramref name="glide"/> seconds, default the
        /// whole duration), optional vibrato and fixed low-pass, and a percussive/sustained envelope.
        /// </summary>
        public void Tone(float t0, float dur, float f0, float f1, Wave wave, float gain, float pan = 0f,
            float attack = 0.002f, float decayDb = 60f, float lowPass = 0f, float q = 0.7f,
            float vibHz = 0f, float vibCents = 0f, float glide = -1f, float drive = 0f)
        {
            StereoBuffer b = Buffer;
            int s0 = Frame(t0);
            int n = Math.Max(2, Frame(dur));
            int end = Math.Min(b.Frames, s0 + n);
            int att = Math.Max(1, Frame(attack));
            int gl = Math.Max(1, Frame(glide > 0f ? glide : dur));
            float mul = (float)Math.Pow(f1 / f0, 1.0 / gl);
            float dcoef = (float)Math.Pow(10.0, -decayDb / 20.0 / Math.Max(1, n - att));
            int fade = Math.Max(1, Math.Min(n / 4, Frame(0.004f)));
            var osc = new Osc(0f);
            var vib = new Osc(0f);
            var lp = new Svf();
            bool useLp = lowPass > 0f;
            if (useLp)
            {
                lp.Set(lowPass, q, _sr);
            }

            float vibDepth = vibCents * 0.000578f;
            float driveNorm = drive > 0f ? 1f / DspMath.Tanh(drive) : 1f;
            DspMath.Pan(pan, out float glL, out float glR);
            glL *= gain;
            glR *= gain;
            float f = f0;
            float amp = 1f;
            float[] l = b.L;
            float[] r = b.R;
            for (int i = 0, j = s0; j < end; i++, j++)
            {
                float ff = f;
                if (vibDepth > 0f)
                {
                    ff *= 1f + vibDepth * vib.Sine(vibHz * _inv);
                }

                float inc = Math.Min(0.45f, ff * _inv);
                float s;
                switch (wave)
                {
                    case Wave.Triangle:
                        s = osc.Triangle(inc);
                        break;
                    case Wave.Square:
                        s = osc.Square(inc);
                        break;
                    case Wave.Saw:
                        s = osc.Saw(inc);
                        break;
                    case Wave.Pulse25:
                        s = osc.Pulse(inc, 0.25f);
                        break;
                    default:
                        s = osc.Sine(inc);
                        break;
                }

                if (drive > 0f)
                {
                    s = DspMath.Tanh(s * drive) * driveNorm;
                }

                if (useLp)
                {
                    s = lp.LowPass(s);
                }

                float e;
                if (i < att)
                {
                    e = (float)i / att;
                }
                else
                {
                    amp *= dcoef;
                    e = amp;
                }

                int left = n - i;
                if (left < fade)
                {
                    e *= (float)left / fade;
                }

                if (i < gl)
                {
                    f *= mul;
                }

                s *= e;
                l[j] += s * glL;
                r[j] += s * glR;
            }
        }

        /// <summary>
        /// Filtered-noise layer with an exponential cutoff sweep, an envelope shape, a pan sweep and optional
        /// stereo decorrelation (<paramref name="width"/> 0 = mono noise, 1 = independent noise per side).
        /// </summary>
        public void NoiseLayer(float t0, float dur, FilterMode mode, float cut0, float cut1, float q, float gain,
            EnvShape shape = EnvShape.Perc, float attack = 0.002f, float decayDb = 60f,
            float pan0 = 0f, float pan1 = 0f, float width = 0.6f, bool pink = false, float drive = 0f)
        {
            StereoBuffer b = Buffer;
            int s0 = Frame(t0);
            int n = Math.Max(2, Frame(dur));
            int end = Math.Min(b.Frames, s0 + n);
            int att = Math.Max(1, Frame(attack));
            float dcoef = (float)Math.Pow(10.0, -decayDb / 20.0 / Math.Max(1, n - att));
            int fade = Math.Max(1, Math.Min(n / 4, Frame(0.005f)));
            var na = new Noise(NextSeed());
            var nb = new Noise(NextSeed());
            var fa = new Svf();
            var fb = new Svf();
            float lcut0 = (float)Math.Log(cut0);
            float lcut1 = (float)Math.Log(cut1);
            float driveNorm = drive > 0f ? 1f / DspMath.Tanh(drive) : 1f;
            float amp = 1f;
            float[] l = b.L;
            float[] r = b.R;
            for (int i = 0, j = s0; j < end; i++, j++)
            {
                float t = (float)i / n;
                if ((i & 7) == 0)
                {
                    float cut = (float)Math.Exp(lcut0 + (lcut1 - lcut0) * t);
                    fa.Set(cut, q, _sr);
                    fb.Set(cut * 1.04f, q, _sr);
                }

                float w1 = pink ? na.Pink() * 2.5f : na.White();
                float w2 = pink ? nb.Pink() * 2.5f : nb.White();
                float xa = w1;
                float xb = w1 * (1f - width) + w2 * width;
                float ya;
                float yb;
                switch (mode)
                {
                    case FilterMode.LowPass:
                        ya = fa.LowPass(xa);
                        yb = fb.LowPass(xb);
                        break;
                    case FilterMode.HighPass:
                        ya = fa.HighPass(xa);
                        yb = fb.HighPass(xb);
                        break;
                    default:
                        ya = fa.BandPass(xa);
                        yb = fb.BandPass(xb);
                        break;
                }

                if (drive > 0f)
                {
                    ya = DspMath.Tanh(ya * drive) * driveNorm;
                    yb = DspMath.Tanh(yb * drive) * driveNorm;
                }

                float e;
                switch (shape)
                {
                    case EnvShape.Swell:
                        e = t * t * t;
                        break;
                    case EnvShape.Bump:
                        float sn = DspMath.Sin01(t * 0.5f);
                        e = sn * sn;
                        break;
                    default:
                        if (i < att)
                        {
                            e = (float)i / att;
                        }
                        else
                        {
                            amp *= dcoef;
                            e = amp;
                        }

                        break;
                }

                int left = n - i;
                if (left < fade)
                {
                    e *= (float)left / fade;
                }

                float pan = pan0 + (pan1 - pan0) * t;
                DspMath.Pan(pan, out float pl, out float pr);
                l[j] += ya * e * pl * gain;
                r[j] += yb * e * pr * gain;
            }
        }

        /// <summary>
        /// Whoosh: band-passed noise whose cutoff rises exponentially to <paramref name="cutPeak"/> at
        /// <paramref name="peakTime"/> and falls to <paramref name="cutEnd"/>, with a matching swell/decay envelope
        /// and a pan sweep that crosses the centre exactly at the loudest point (the "something just flew past" gesture).
        /// </summary>
        public void Whoosh(float t0, float dur, float cutStart, float cutPeak, float cutEnd, float peakTime, float q, float gain,
            float pan0, float pan1, float width, bool pink = false, float body = 0.25f)
        {
            StereoBuffer b = Buffer;
            int s0 = Frame(t0);
            int n = Math.Max(2, Frame(dur));
            int pk = Math.Max(1, Math.Min(n - 1, Frame(peakTime)));
            int end = Math.Min(b.Frames, s0 + n);
            var na = new Noise(NextSeed());
            var nb = new Noise(NextSeed());
            var fa = new Svf();
            var fb = new Svf();
            float l0 = (float)Math.Log(cutStart);
            float l1 = (float)Math.Log(cutPeak);
            float l2 = (float)Math.Log(cutEnd);
            float dcoef = (float)Math.Pow(10.0, -50.0 / 20.0 / Math.Max(1, n - pk));
            float amp = 1f;
            int fade = Math.Max(1, Math.Min(n / 4, Frame(0.006f)));
            for (int i = 0, j = s0; j < end; i++, j++)
            {
                if ((i & 7) == 0)
                {
                    float lc = i < pk ? l0 + (l1 - l0) * DspMath.Smooth((float)i / pk) : l1 + (l2 - l1) * ((float)(i - pk) / (n - pk));
                    float cut = (float)Math.Exp(lc);
                    fa.Set(cut, q, _sr);
                    fb.Set(cut * 1.05f, q, _sr);
                }

                float e;
                if (i < pk)
                {
                    float x = (float)i / pk;
                    e = x * (float)Math.Sqrt(x);
                }
                else
                {
                    amp *= dcoef;
                    e = amp;
                }

                int left = n - i;
                if (left < fade)
                {
                    e *= (float)left / fade;
                }

                float w1 = pink ? na.Pink() * 2.5f : na.White();
                float w2 = pink ? nb.Pink() * 2.5f : nb.White();
                fa.Process(w1);
                fb.Process(w1 * (1f - width) + w2 * width);
                float ya = fa.Band * (1f / q) + fa.Low * body;
                float yb = fb.Band * (1f / q) + fb.Low * body;
                float mid = 0.5f * (pan0 + pan1);
                float pan = i < pk ? pan0 + (mid - pan0) * DspMath.Smooth((float)i / pk)
                    : mid + (pan1 - mid) * DspMath.Smooth((float)(i - pk) / (n - pk));
                DspMath.Pan(pan, out float pl, out float pr);
                b.L[j] += ya * e * pl * gain;
                b.R[j] += yb * e * pr * gain;
            }
        }

        /// <summary>
        /// Swept synth tone: one or two (detuned, spread) oscillators with exponential pitch glide through a resonant
        /// low-pass whose cutoff sweeps exponentially and can wobble with an LFO.
        /// </summary>
        public void SweepTone(float t0, float dur, float f0, float f1, Wave wave, float gain, float pan, float cut0, float cut1, float q,
            float detuneCents = 0f, float attack = 0.002f, float decayDb = 40f, float lfoHz = 0f, float lfoOct = 0f, float glide = -1f)
        {
            StereoBuffer b = Buffer;
            int s0 = Frame(t0);
            int n = Math.Max(2, Frame(dur));
            int end = Math.Min(b.Frames, s0 + n);
            int att = Math.Max(1, Frame(attack));
            int gl = Math.Max(1, Frame(glide > 0f ? glide : dur));
            float mul = (float)Math.Pow(f1 / f0, 1.0 / gl);
            float dcoef = (float)Math.Pow(10.0, -decayDb / 20.0 / Math.Max(1, n - att));
            int fade = Math.Max(1, Math.Min(n / 4, Frame(0.005f)));
            bool two = detuneCents > 0f;
            float da = DspMath.Cents(-detuneCents);
            float db = DspMath.Cents(detuneCents);
            var oa = new Osc(0f);
            var ob = new Osc(0.5f);
            var lfo = new Osc(0f);
            var fa = new Svf();
            var fb = new Svf();
            float lc0 = (float)Math.Log(cut0);
            float lc1 = (float)Math.Log(cut1);
            DspMath.Pan(two ? pan - 0.35f : pan, out float aL, out float aR);
            DspMath.Pan(two ? pan + 0.35f : pan, out float bL, out float bR);
            float f = f0;
            float amp = 1f;
            float lfoVal = 0f;
            for (int i = 0, j = s0; j < end; i++, j++)
            {
                if (lfoHz > 0f)
                {
                    lfoVal = lfo.Sine(lfoHz * _inv);
                }

                if ((i & 7) == 0)
                {
                    float cut = (float)Math.Exp(lc0 + (lc1 - lc0) * ((float)i / n)) * Voices.Exp2(lfoOct * lfoVal);
                    fa.Set(cut, q, _sr);
                    fb.Set(cut, q, _sr);
                }

                float ia = Math.Min(0.45f, f * da * _inv);
                float ib = Math.Min(0.45f, f * db * _inv);
                float va = Wav(ref oa, wave, two ? ia : f * _inv);
                float vb = two ? Wav(ref ob, wave, ib) : 0f;
                if (i < gl)
                {
                    f *= mul;
                }

                float e;
                if (i < att)
                {
                    e = (float)i / att;
                }
                else
                {
                    amp *= dcoef;
                    e = amp;
                }

                int left = n - i;
                if (left < fade)
                {
                    e *= (float)left / fade;
                }

                float ya = fa.LowPass(va) * e * gain;
                float yb = two ? fb.LowPass(vb) * e * gain : 0f;
                b.L[j] += ya * aL + yb * bL;
                b.R[j] += ya * aR + yb * bR;
            }
        }

        private static float Wav(ref Osc o, Wave wave, float inc)
        {
            switch (wave)
            {
                case Wave.Triangle:
                    return o.Triangle(inc);
                case Wave.Square:
                    return o.Square(inc);
                case Wave.Saw:
                    return o.Saw(inc);
                case Wave.Pulse25:
                    return o.Pulse(inc, 0.25f);
                default:
                    return o.Sine(inc);
            }
        }

        /// <summary>Sine boom with exponential pitch drop, saturation and optional low-pass.</summary>
        public void Boom(float t0, float dur, float f0, float f1, float pitchTime, float gain, float drive = 1f, float lowPass = 0f)
        {
            StereoBuffer b = Buffer;
            int s0 = Frame(t0);
            int n = Math.Max(2, Frame(dur));
            int end = Math.Min(b.Frames, s0 + n);
            int att = Math.Max(1, Frame(0.002f));
            int pt = Math.Max(1, Frame(pitchTime));
            float mul = (float)Math.Pow(f1 / f0, 1.0 / pt);
            float dcoef = DspMath.DecayCoef(dur, _sr);
            int fade = Math.Max(1, Frame(0.01f));
            var lp = new Svf();
            bool useLp = lowPass > 0f;
            if (useLp)
            {
                lp.Set(lowPass, 0.7f, _sr);
            }

            float driveNorm = drive > 0f ? 1f / DspMath.Tanh(drive) : 1f;
            float phase = 0f;
            float f = f0;
            float amp = 1f;
            for (int i = 0, j = s0; j < end; i++, j++)
            {
                phase += f * _inv;
                if (phase >= 1f)
                {
                    phase -= 1f;
                }

                if (i < pt)
                {
                    f *= mul;
                }

                float s = DspMath.Sin01(phase) * amp;
                amp *= dcoef;
                if (drive > 0f)
                {
                    s = DspMath.Tanh(s * drive) * driveNorm;
                }

                if (useLp)
                {
                    s = lp.LowPass(s);
                }

                if (i < att)
                {
                    s *= (float)i / att;
                }

                int left = n - i;
                if (left < fade)
                {
                    s *= (float)left / fade;
                }

                s *= gain;
                b.L[j] += s;
                b.R[j] += s;
            }
        }

        public void Bell(float t0, float freq, float gain, float pan, float decay, float ratio = 3.5f, float index = 0.5f, float shimmer = 0.25f)
        {
            Voices.Bell(Buffer, Frame(t0), freq, gain, pan, decay, ratio, index, _sr, 0.0015f, shimmer);
        }

        public void Synth(float t0, float gateSeconds, float freq, float gain, float pan, in SynthPatch patch)
        {
            Voices.Subtractive(Buffer, Frame(t0), Math.Max(1, Frame(gateSeconds)), freq, gain, pan, patch, NextSeed(), _sr);
        }

        /// <summary>A pad chord (one shared voice) at full rate.</summary>
        public void Chord(float t0, float gateSeconds, int[] midi, float gain, in PadPatch patch)
        {
            var freqs = new float[midi.Length];
            var gains = new float[midi.Length];
            for (int i = 0; i < midi.Length; i++)
            {
                freqs[i] = DspMath.MidiToHz(midi[i]);
                gains[i] = 1f;
            }

            Voices.PadChord(Buffer, Frame(t0), Math.Max(1, Frame(gateSeconds)), freqs, gains, gain, patch, NextSeed(), _sr);
        }

        /// <summary>Glass/debris: many short inharmonic pings, denser at the start.</summary>
        public void Shatter(float t0, float spread, int count, float fMin, float fMax, float decayMin, float decayMax, float gain)
        {
            for (int k = 0; k < count; k++)
            {
                float u = Rng.Value();
                float t = t0 + spread * u * u;
                float f = fMin * (float)Math.Pow(fMax / fMin, Rng.Value());
                float d = Rng.Range(decayMin, decayMax);
                float g = gain * (1f - 0.6f * u) * Rng.Range(0.5f, 1f);
                float pan = Rng.Range(-0.9f, 0.9f);
                Tone(t, d, f, f * Rng.Range(0.97f, 1.01f), Wave.Sine, g, pan, 0.0005f);
                Tone(t, d * 0.6f, f * Rng.Range(1.47f, 2.73f), f * 2f, Wave.Sine, g * 0.45f, -pan, 0.0005f);
            }
        }

        /// <summary>Crackle/debris grains: short band-passed noise bursts scattered over time.</summary>
        public void Crackle(float t0, float spread, int count, float fMin, float fMax, float gain)
        {
            for (int k = 0; k < count; k++)
            {
                float u = Rng.Value();
                float t = t0 + spread * (float)Math.Pow(u, 1.6);
                float f = fMin * (float)Math.Pow(fMax / fMin, Rng.Value());
                float d = Rng.Range(0.004f, 0.03f);
                float g = gain * (1f - 0.75f * u) * Rng.Range(0.4f, 1f);
                float pan = Rng.Range(-0.9f, 0.9f);
                NoiseLayer(t, d, FilterMode.BandPass, f, f * 0.8f, 2.5f, g, EnvShape.Perc, 0.0005f, 40f, pan, pan, 0.2f);
            }
        }

        public void Reverb(float size, float rt60, float dampHz, float send, float preDelayMs = 10f)
        {
            var rv = new Reverb(_sr, size, rt60, dampHz, preDelayMs);
            rv.Process(Buffer.L, Buffer.R, Buffer.L, Buffer.R, 0, Buffer.Frames, send, 1f);
        }

        public void Delay(float seconds, float feedback, float send, float lowPassHz = 4000f, float highPassHz = 250f)
        {
            var d = new PingPongDelay(Math.Max(1, Frame(seconds)), feedback, lowPassHz, highPassHz, _sr);
            d.Process(Buffer.L, Buffer.R, Buffer.L, Buffer.R, 0, Buffer.Frames, send, 1f);
        }

        public void Chorus(float baseMs, float depthMs, float rateHz, float wet)
        {
            var c = new Chorus(_sr, baseMs, depthMs, rateHz);
            c.Process(Buffer.L, Buffer.R, Buffer.L, Buffer.R, 0, Buffer.Frames, wet);
        }

        /// <summary>
        /// Removes DC, trims trailing silence (below −70 dB of the peak), fades the edges, levels the clip and
        /// applies a safety limiter. Loudness-targeted levels may push up to <see cref="SfxLevel.MaxPushDb"/> into a
        /// soft clipper for density (impacts); otherwise the peak ceiling wins.
        /// </summary>
        public RenderedAudio Finish(string name, SfxLevel level)
        {
            StereoBuffer b = Buffer;
            b.RemoveDc(12f, _sr);
            float peak = b.Peak(b.Frames);
            int last = b.Frames;
            if (peak > 0f)
            {
                float floor = peak * 0.0003f;
                last = 0;
                for (int i = b.Frames - 1; i >= 0; i--)
                {
                    if (Math.Abs(b.L[i]) > floor || Math.Abs(b.R[i]) > floor)
                    {
                        last = i + 1;
                        break;
                    }
                }
            }

            int fadeLen = Math.Max(1, Frame(0.02f));
            int frames = Math.Min(b.Frames, Math.Max(fadeLen * 2, last + fadeLen));
            b.FadeOut(frames - fadeLen, fadeLen);
            b.FadeIn(Math.Max(1, Frame(0.0005f)));

            float ceiling = DspMath.DbToGain(level.CeilingDb);
            if (level.ByLoudness)
            {
                float lufs = Loudness.MaxMomentary(b, frames, _sr);
                peak = b.Peak(frames);
                if (peak > 1e-9f && lufs > -119f)
                {
                    float gain = DspMath.DbToGain(level.TargetLufs - lufs);
                    float maxGain = ceiling / peak * DspMath.DbToGain(level.MaxPushDb);
                    if (gain > maxGain)
                    {
                        gain = maxGain;
                    }

                    b.Scale(gain, 0, frames);
                    if (peak * gain > ceiling * 0.8f)
                    {
                        // Soft-clip the overs into the ceiling (adds density to impacts instead of just limiting).
                        float inv = 1f / ceiling;
                        for (int i = 0; i < frames; i++)
                        {
                            b.L[i] = DspMath.SoftClip(b.L[i] * inv, 0.8f) * ceiling;
                            b.R[i] = DspMath.SoftClip(b.R[i] * inv, 0.8f) * ceiling;
                        }
                    }
                }
            }
            else
            {
                b.NormalizePeak(level.CeilingDb, frames);
            }

            var lim = new Limiter(-0.1f, 0.05f, _sr);
            lim.Process(b.L, b.R, 0, frames);
            return b.ToRendered(name, frames, SampleRate, false, false);
        }

        /// <summary>
        /// Builds a seamless loop from a canvas laid out as [pre-roll | loop | crossfade]: the pre-roll (start-up
        /// transients) is dropped and the material rendered past the loop end is cross-faded over the loop start, so
        /// the last frame flows into the first. Use equal-power for noise, linear for periodic/tonal content.
        /// </summary>
        public StereoBuffer CrossfadeLoop(float preRollSeconds, float loopSeconds, float crossfadeSeconds, bool equalPower)
        {
            StereoBuffer b = Buffer;
            int pre = Frame(preRollSeconds);
            int loop = Frame(loopSeconds);
            int xf = Math.Max(1, Math.Min(Frame(crossfadeSeconds), b.Frames - pre - loop));
            var outBuf = new StereoBuffer(loop);
            Array.Copy(b.L, pre, outBuf.L, 0, loop);
            Array.Copy(b.R, pre, outBuf.R, 0, loop);
            for (int i = 0; i < xf; i++)
            {
                float t = (float)i / xf;
                float gin = equalPower ? DspMath.Sin01(t * 0.25f) : t;
                float gout = equalPower ? DspMath.Cos01(t * 0.25f) : 1f - t;
                outBuf.L[i] = b.L[pre + i] * gin + b.L[pre + loop + i] * gout;
                outBuf.R[i] = b.R[pre + i] * gin + b.R[pre + loop + i] * gout;
            }

            return outBuf;
        }
    }
}

using System;
using Unity.IL2CPP.CompilerServices;

namespace BeamRunner.Core.Audio
{
    /// <summary>
    /// Seamless ambience loops. Tonal layers use frequencies/LFO rates with an integer number of cycles per loop
    /// and are rendered after a pre-roll (so filters are in steady state) — they wrap perfectly. Noise layers are
    /// rendered past the loop end and equal-power cross-faded over the start.
    /// </summary>
    [Il2CppSetOption(Option.NullChecks, false)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
    internal static class LoopRenderer
    {
        public static RenderedAudio Render(LoopId id, int sampleRate, int seed)
        {
            switch (id)
            {
                case LoopId.Slipstream:
                    return Slipstream(sampleRate, seed);
                default:
                    return Engine(sampleRate, seed);
            }
        }

        /// <summary>Ship engine: detuned saw drone + sub + faint turbine whine + rumbling filtered noise (2 s loop).</summary>
        private static RenderedAudio Engine(int sampleRate, int seed)
        {
            const float LoopSec = 2f;
            const float PreSec = 0.25f;
            const float XfSec = 0.3f;
            float sr = sampleRate;
            float inv = 1f / sr;
            int loop = (int)Math.Round(LoopSec * sr);
            int pre = (int)(PreSec * sr);

            // Tonal layer: every frequency × 2 s is an integer number of cycles.
            var tonal = new StereoBuffer(pre + loop);
            var sawA = new Osc(0f);
            var sawB = new Osc(0.33f);
            var sawC = new Osc(0.66f);
            var sub = new Osc(0f);
            var w1 = new Osc(0f);
            var w2 = new Osc(0.5f);
            var lfo = new Osc(0f);
            var trem = new Osc(0f);
            var fl = new Svf();
            var fr = new Svf();
            for (int i = 0; i < tonal.Frames; i++)
            {
                float lv = lfo.Sine(1f * inv);
                if ((i & 15) == 0)
                {
                    float cut = 620f * Voices.Exp2(0.3f * lv);
                    fl.Set(cut, 1.1f, sr);
                    fr.Set(cut * 1.06f, 1.1f, sr);
                }

                float a = sawA.Saw(55.5f * inv);
                float b = sawB.Saw(54.5f * inv);
                float c = sawC.Saw(110.5f * inv) * 0.5f;
                float s = sub.Sine(55f * inv) * 0.55f;
                float tr = 0.5f + 0.5f * trem.Sine(0.5f * inv);
                float wh = w1.Sine(880f * inv) * 0.035f * tr + w2.Sine(1320.5f * inv) * 0.022f * (1f - tr);
                tonal.L[i] = fl.LowPass(a * 0.8f + b * 0.45f + c) * 0.5f + s + wh;
                tonal.R[i] = fr.LowPass(a * 0.45f + b * 0.8f + c) * 0.5f + s + wh;
            }

            // Noise layer: rumbling low-passed pink noise with an 8 Hz flutter (16 cycles per loop).
            var kit = new SfxKit(sampleRate, PreSec + LoopSec + XfSec + 0.01f, seed);
            StereoBuffer nb = kit.Buffer;
            var nl = new Noise((uint)seed ^ 0xE1u);
            var nr = new Noise((uint)seed ^ 0xE2u);
            var ll = new Svf();
            var lr = new Svf();
            ll.Set(480f, 0.9f, sr);
            lr.Set(520f, 0.9f, sr);
            var bl = new Svf();
            var br = new Svf();
            bl.Set(1150f, 1.4f, sr);
            br.Set(1250f, 1.4f, sr);
            var flutter = new Osc(0f);
            for (int i = 0; i < nb.Frames; i++)
            {
                float am = 0.8f + 0.2f * flutter.Sine(8f * inv);
                float xl = nl.Pink() * 2.5f;
                float xr = nr.Pink() * 2.5f;
                nb.L[i] = (ll.LowPass(xl) * 0.5f + bl.BandPass(xl) * 0.12f) * am;
                nb.R[i] = (lr.LowPass(xr) * 0.5f + br.BandPass(xr) * 0.12f) * am;
            }

            StereoBuffer outBuf = kit.CrossfadeLoop(PreSec, LoopSec, XfSec, true);
            for (int i = 0; i < loop && i < outBuf.Frames; i++)
            {
                outBuf.L[i] += tonal.L[pre + i];
                outBuf.R[i] += tonal.R[pre + i];
            }

            outBuf.NormalizePeak(-3f, outBuf.Frames);
            return outBuf.ToRendered("Loop_Engine", outBuf.Frames, sampleRate, true, false);
        }

        /// <summary>Slipstream: airy band-passed pink wind with a slow sweep, hiss flutter and shimmering partials (3 s loop).</summary>
        private static RenderedAudio Slipstream(int sampleRate, int seed)
        {
            const float LoopSec = 3f;
            const float PreSec = 0.2f;
            const float XfSec = 0.5f;
            float sr = sampleRate;
            float inv = 1f / sr;
            int loop = (int)Math.Round(LoopSec * sr);
            int pre = (int)(PreSec * sr);

            var kit = new SfxKit(sampleRate, PreSec + LoopSec + XfSec + 0.01f, seed);
            StereoBuffer nb = kit.Buffer;
            var nl = new Noise((uint)seed ^ 0x51u);
            var nr = new Noise((uint)seed ^ 0x52u);
            var hl = new Noise((uint)seed ^ 0x53u);
            var hr = new Noise((uint)seed ^ 0x54u);
            var bl = new Svf();
            var br = new Svf();
            var b2l = new Svf();
            var b2r = new Svf();
            var hpl = new Svf();
            var hpr = new Svf();
            hpl.Set(7000f, 0.7f, sr);
            hpr.Set(7400f, 0.7f, sr);
            float sweepInc = 1f / LoopSec * inv;
            var hissLfo = new Osc(0f);
            for (int i = 0; i < nb.Frames; i++)
            {
                float t = (i - pre) * sweepInc;
                t -= (float)Math.Floor(t);
                if ((i & 15) == 0)
                {
                    float cl = 1600f * Voices.Exp2(0.9f * DspMath.Sin01(t));
                    float cr = 1600f * Voices.Exp2(0.9f * DspMath.Sin01(DspMath.Wrap01(t + 0.25f)));
                    bl.Set(cl, 0.8f, sr);
                    br.Set(cr, 0.8f, sr);
                    b2l.Set(cr * 2.7f, 1.5f, sr);
                    b2r.Set(cl * 2.7f, 1.5f, sr);
                }

                float xl = nl.Pink() * 2.5f;
                float xr = nr.Pink() * 2.5f;
                float hiss = 0.6f + 0.4f * hissLfo.Sine(2f * inv);
                nb.L[i] = bl.BandPass(xl) * 0.6f + b2l.BandPass(xl) * 0.25f + hpl.HighPass(hl.White()) * 0.12f * hiss;
                nb.R[i] = br.BandPass(xr) * 0.6f + b2r.BandPass(xr) * 0.25f + hpr.HighPass(hr.White()) * 0.12f * (1.6f - hiss);
            }

            StereoBuffer outBuf = kit.CrossfadeLoop(PreSec, LoopSec, XfSec, true);

            // Shimmer partials (A6, E7, A7) with tremolos at 9, 13 and 17 cycles per loop.
            var s1 = new Osc(0f);
            var s2 = new Osc(0f);
            var s3 = new Osc(0f);
            var t1 = new Osc(0f);
            var t2 = new Osc(0.3f);
            var t3 = new Osc(0.6f);
            for (int i = 0; i < loop && i < outBuf.Frames; i++)
            {
                float a = s1.Sine(1760f * inv) * (0.5f + 0.5f * t1.Sine(3f * inv)) * 0.035f;
                float b = s2.Sine(2637f * inv) * (0.5f + 0.5f * t2.Sine(13f / 3f * inv)) * 0.025f;
                float c = s3.Sine(3520f * inv) * (0.5f + 0.5f * t3.Sine(17f / 3f * inv)) * 0.016f;
                outBuf.L[i] += a * 1f + b * 0.4f + c * 0.7f;
                outBuf.R[i] += a * 0.4f + b * 1f + c * 0.7f;
            }

            outBuf.NormalizePeak(-8f, outBuf.Frames);
            return outBuf.ToRendered("Loop_Slipstream", outBuf.Frames, sampleRate, true, false);
        }
    }
}

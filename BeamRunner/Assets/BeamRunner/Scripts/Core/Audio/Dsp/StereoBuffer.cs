using System;
using Unity.IL2CPP.CompilerServices;

namespace BeamRunner.Core.Audio
{
    /// <summary>
    /// Planar stereo work buffer used while rendering, with the loop/fade/level utilities the renderers
    /// share. Converted to interleaved <see cref="RenderedAudio"/> at the end.
    /// </summary>
    [Il2CppSetOption(Option.NullChecks, false)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
    public sealed class StereoBuffer
    {
        public StereoBuffer(int frames)
        {
            Frames = Math.Max(1, frames);
            L = new float[Frames];
            R = new float[Frames];
        }

        public int Frames { get; }

        public float[] L { get; }

        public float[] R { get; }

        public void Clear()
        {
            Array.Clear(L, 0, Frames);
            Array.Clear(R, 0, Frames);
        }

        /// <summary>Adds another buffer (same or shorter length) scaled by <paramref name="gain"/>.</summary>
        public void Mix(StereoBuffer other, float gain)
        {
            int n = Math.Min(Frames, other.Frames);
            float[] l = L;
            float[] r = R;
            float[] ol = other.L;
            float[] oR = other.R;
            for (int i = 0; i < n; i++)
            {
                l[i] += ol[i] * gain;
                r[i] += oR[i] * gain;
            }
        }

        public void Scale(float gain)
        {
            Scale(gain, 0, Frames);
        }

        public void Scale(float gain, int start, int count)
        {
            float[] l = L;
            float[] r = R;
            int end = Math.Min(Frames, start + count);
            for (int i = start; i < end; i++)
            {
                l[i] *= gain;
                r[i] *= gain;
            }
        }

        /// <summary>Multiplies both channels by a per-frame gain curve.</summary>
        public void Multiply(float[] curve)
        {
            int n = Math.Min(Frames, curve.Length);
            float[] l = L;
            float[] r = R;
            for (int i = 0; i < n; i++)
            {
                float g = curve[i];
                l[i] *= g;
                r[i] *= g;
            }
        }

        /// <summary>Raised-cosine fade to silence over [start, start+count); everything after is zeroed.</summary>
        public void FadeOut(int start, int count)
        {
            if (start < 0)
            {
                start = 0;
            }

            int end = Math.Min(Frames, start + count);
            for (int i = start; i < end; i++)
            {
                float t = (float)(i - start) / count;
                float g = 0.5f + 0.5f * DspMath.Cos01(t * 0.5f);
                L[i] *= g;
                R[i] *= g;
            }

            for (int i = end; i < Frames; i++)
            {
                L[i] = 0f;
                R[i] = 0f;
            }
        }

        /// <summary>Short raised-cosine fade-in from silence (anti-click).</summary>
        public void FadeIn(int count)
        {
            int end = Math.Min(Frames, count);
            for (int i = 0; i < end; i++)
            {
                float t = (float)i / count;
                float g = 0.5f - 0.5f * DspMath.Cos01(t * 0.5f);
                L[i] *= g;
                R[i] *= g;
            }
        }

        /// <summary>
        /// Wraps everything past <paramref name="loopFrames"/> back onto the start (modulo the loop), so
        /// release/reverb/delay tails of the last bars continue seamlessly into the first bars.
        /// </summary>
        public void FoldTail(int loopFrames)
        {
            float[] l = L;
            float[] r = R;
            int j = 0;
            for (int i = loopFrames; i < Frames; i++)
            {
                l[j] += l[i];
                r[j] += r[i];
                l[i] = 0f;
                r[i] = 0f;
                if (++j >= loopFrames)
                {
                    j = 0;
                }
            }
        }

        /// <summary>Removes DC with a one-pole high-pass (~<paramref name="hz"/>).</summary>
        public void RemoveDc(float hz, float sampleRate)
        {
            OnePole pl = OnePole.FromHz(hz, sampleRate);
            OnePole pr = OnePole.FromHz(hz, sampleRate);
            for (int i = 0; i < Frames; i++)
            {
                // The offset keeps the filter state out of the denormal range during silence.
                L[i] = pl.HighPass(L[i] + DspMath.AntiDenormal);
                R[i] = pr.HighPass(R[i] + DspMath.AntiDenormal);
            }
        }

        public float Peak(int frames)
        {
            int n = Math.Min(frames, Frames);
            float p = 0f;
            for (int i = 0; i < n; i++)
            {
                float a = Math.Abs(L[i]);
                float b = Math.Abs(R[i]);
                if (a > p)
                {
                    p = a;
                }

                if (b > p)
                {
                    p = b;
                }
            }

            return p;
        }

        public float Rms(int frames)
        {
            int n = Math.Min(frames, Frames);
            double s = 0.0;
            for (int i = 0; i < n; i++)
            {
                s += L[i] * (double)L[i] + R[i] * (double)R[i];
            }

            return n == 0 ? 0f : (float)Math.Sqrt(s / (2.0 * n));
        }

        /// <summary>Scales the first <paramref name="frames"/> so the peak hits <paramref name="peakDb"/> dBFS.</summary>
        public void NormalizePeak(float peakDb, int frames)
        {
            float p = Peak(frames);
            if (p > 1e-9f)
            {
                Scale(DspMath.DbToGain(peakDb) / p, 0, frames);
            }
        }

        /// <summary>
        /// Bus saturation: samples above <paramref name="knee"/>·(<paramref name="headroom"/>·peak) are soft-clipped so the
        /// loudest transients come down by roughly (1 − <paramref name="headroom"/>) while quieter material is untouched.
        /// Stateless, so it keeps loops seamless.
        /// </summary>
        public void Saturate(float headroom, float knee)
        {
            float peak = Peak(Frames);
            if (peak <= 1e-9f)
            {
                return;
            }

            float scale = headroom * peak;
            float inv = 1f / scale;
            for (int i = 0; i < Frames; i++)
            {
                L[i] = DspMath.SoftClip(L[i] * inv, knee) * scale;
                R[i] = DspMath.SoftClip(R[i] * inv, knee) * scale;
            }
        }

        /// <summary>Soft-clips (never above ±1) the first <paramref name="frames"/>.</summary>
        public void SoftClip(float knee, int frames)
        {
            int n = Math.Min(frames, Frames);
            for (int i = 0; i < n; i++)
            {
                L[i] = DspMath.SoftClip(L[i], knee);
                R[i] = DspMath.SoftClip(R[i], knee);
            }
        }

        /// <summary>Interleaves the first <paramref name="frames"/> into a <see cref="RenderedAudio"/> (mono sums L+R).</summary>
        public RenderedAudio ToRendered(string name, int frames, int sampleRate, bool isLoop, bool mono)
        {
            int n = Math.Min(frames, Frames);
            if (mono)
            {
                var m = new float[n];
                for (int i = 0; i < n; i++)
                {
                    m[i] = 0.5f * (L[i] + R[i]);
                }

                return new RenderedAudio(name, m, 1, sampleRate, isLoop);
            }

            var s = new float[n * 2];
            for (int i = 0, j = 0; i < n; i++, j += 2)
            {
                s[j] = L[i];
                s[j + 1] = R[i];
            }

            return new RenderedAudio(name, s, 2, sampleRate, isLoop);
        }
    }
}

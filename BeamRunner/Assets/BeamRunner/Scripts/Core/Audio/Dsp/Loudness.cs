using System;
using Unity.IL2CPP.CompilerServices;

namespace BeamRunner.Core.Audio
{
    /// <summary>
    /// ITU-R BS.1770 style loudness helpers (K-weighting = +4 dB high shelf at 1.5 kHz and a 38 Hz high-pass).
    /// Used to level sound effects by perceived loudness rather than by peak.
    /// </summary>
    [Il2CppSetOption(Option.NullChecks, false)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
    public static class Loudness
    {
        /// <summary>
        /// Maximum momentary loudness (LUFS, 400 ms windows, 100 ms hop) of the first <paramref name="frames"/>.
        /// Clips shorter than a window are measured as one zero-padded window, like a meter would.
        /// </summary>
        public static float MaxMomentary(StereoBuffer b, int frames, float sampleRate)
        {
            frames = Math.Min(frames, b.Frames);
            if (frames <= 0)
            {
                return -120f;
            }

            Biquad shelfL = Biquad.HighShelf(1500.0, 4.0, sampleRate);
            Biquad shelfR = Biquad.HighShelf(1500.0, 4.0, sampleRate);
            Biquad hpL = Biquad.HighPass(38.0, 0.5, sampleRate);
            Biquad hpR = Biquad.HighPass(38.0, 0.5, sampleRate);
            var cum = new double[frames + 1];
            double acc = 0.0;
            for (int i = 0; i < frames; i++)
            {
                float l = hpL.Process(shelfL.Process(b.L[i]));
                float r = hpR.Process(shelfR.Process(b.R[i]));
                acc += l * (double)l + r * (double)r;
                cum[i + 1] = acc;
            }

            int win = Math.Max(1, (int)(0.4f * sampleRate));
            int hop = Math.Max(1, win / 4);
            double best = 0.0;
            if (frames <= win)
            {
                best = cum[frames] / win;
            }
            else
            {
                for (int s = 0; s + win <= frames; s += hop)
                {
                    double ms = (cum[s + win] - cum[s]) / win;
                    if (ms > best)
                    {
                        best = ms;
                    }
                }
            }

            return best <= 1e-12 ? -120f : (float)(-0.691 + 10.0 * Math.Log10(best));
        }
    }
}

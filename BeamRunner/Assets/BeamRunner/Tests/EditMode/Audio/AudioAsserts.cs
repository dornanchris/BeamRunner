using System;
using BeamRunner.Core.Audio;
using NUnit.Framework;

namespace BeamRunner.Core.Tests.Audio
{
    /// <summary>Shared assertions for rendered audio.</summary>
    internal static class AudioAsserts
    {
        public static void Finite(RenderedAudio a)
        {
            float[] s = a.Samples;
            for (int i = 0; i < s.Length; i++)
            {
                if (float.IsNaN(s[i]) || float.IsInfinity(s[i]))
                {
                    Assert.Fail(a.Name + ": non-finite sample at " + i);
                }
            }
        }

        /// <summary>True for a mono clip or a stereo clip whose two channels are bit-identical.</summary>
        public static bool IsMonoContent(RenderedAudio a)
        {
            if (a.Channels == 1)
            {
                return true;
            }

            float[] s = a.Samples;
            for (int i = 0; i + 1 < s.Length; i += 2)
            {
                if (s[i] != s[i + 1])
                {
                    return false;
                }
            }

            return a.Channels == 2;
        }

        /// <summary>One-shots must start and end near silence (no clicks when triggered or cut).</summary>
        public static void EdgesQuiet(RenderedAudio a)
        {
            int ch = a.Channels;
            for (int c = 0; c < ch; c++)
            {
                Assert.Less(Math.Abs(a.Samples[c]), 0.02f, a.Name + " starts with a click");
                Assert.Less(Math.Abs(a.Samples[a.Samples.Length - ch + c]), 0.001f, a.Name + " ends abruptly");
            }
        }

        /// <summary>
        /// The jump from the last frame back to the first must look like any ordinary step inside the clip: no
        /// larger than the 99.9th percentile of adjacent-sample differences (sampled for speed).
        /// </summary>
        public static void Seamless(RenderedAudio a)
        {
            int ch = a.Channels;
            int frames = a.FrameCount;
            float[] s = a.Samples;
            int stride = Math.Max(1, frames / 60000);
            int count = 0;
            var steps = new float[(frames / stride + 1) * ch];
            for (int i = 1; i < frames; i += stride)
            {
                for (int c = 0; c < ch; c++)
                {
                    steps[count++] = Math.Abs(s[i * ch + c] - s[(i - 1) * ch + c]);
                }
            }

            Array.Sort(steps, 0, count);
            float p999 = steps[Math.Min(count - 1, (int)(count * 0.999))];
            for (int c = 0; c < ch; c++)
            {
                float seam = Math.Abs(s[c] - s[(frames - 1) * ch + c]);
                Assert.LessOrEqual(seam, p999 + 1e-4f, a.Name + " loop seam jumps (" + seam + " vs typical " + p999 + ")");
            }
        }
    }
}

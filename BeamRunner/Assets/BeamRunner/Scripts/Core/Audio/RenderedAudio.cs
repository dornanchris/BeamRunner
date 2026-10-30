using System;

namespace BeamRunner.Core.Audio
{
    /// <summary>
    /// A rendered sound: interleaved float PCM in [−1, 1] plus its format. Plain data, safe to hand from a
    /// worker thread to the main thread (it is never mutated after rendering).
    /// </summary>
    public sealed class RenderedAudio
    {
        public RenderedAudio(string name, float[] samples, int channels, int sampleRate, bool isLoop)
        {
            if (samples == null)
            {
                throw new ArgumentNullException(nameof(samples));
            }

            if (channels < 1 || samples.Length % channels != 0)
            {
                throw new ArgumentException("Sample count must be a multiple of the channel count.");
            }

            Name = name ?? string.Empty;
            Samples = samples;
            Channels = channels;
            SampleRate = sampleRate;
            IsLoop = isLoop;
        }

        /// <summary>Clip name (used for the Unity AudioClip).</summary>
        public string Name { get; }

        /// <summary>Interleaved samples (frame-major: L, R, L, R, …).</summary>
        public float[] Samples { get; }

        public int Channels { get; }

        public int SampleRate { get; }

        /// <summary>True if the clip is designed to loop seamlessly (last frame flows into the first).</summary>
        public bool IsLoop { get; }

        /// <summary>Number of sample frames (samples per channel).</summary>
        public int FrameCount => Samples.Length / Channels;

        public double DurationSeconds => (double)FrameCount / SampleRate;

        /// <summary>Absolute peak over all samples.</summary>
        public float Peak()
        {
            float p = 0f;
            float[] s = Samples;
            for (int i = 0; i < s.Length; i++)
            {
                float a = s[i] < 0f ? -s[i] : s[i];
                if (a > p)
                {
                    p = a;
                }
            }

            return p;
        }

        /// <summary>RMS over all samples.</summary>
        public float Rms()
        {
            double sum = 0.0;
            float[] s = Samples;
            for (int i = 0; i < s.Length; i++)
            {
                sum += s[i] * (double)s[i];
            }

            return s.Length == 0 ? 0f : (float)Math.Sqrt(sum / s.Length);
        }
    }
}

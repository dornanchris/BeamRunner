using Unity.IL2CPP.CompilerServices;

namespace BeamRunner.Core.Audio
{
    /// <summary>
    /// Look-ahead-free stereo peak limiter: instant gain reduction when a peak would exceed the ceiling
    /// (so output never exceeds it), smooth exponential release, linked channels. Designed as a
    /// safety net — levels are balanced so it rarely engages.
    /// </summary>
    [Il2CppSetOption(Option.NullChecks, false)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
    public sealed class Limiter
    {
        private readonly float _ceiling;
        private readonly float _release;
        private float _gain = 1f;

        /// <param name="ceilingDb">Maximum output level in dBFS (e.g. −1).</param>
        /// <param name="releaseSeconds">Time constant for gain recovery.</param>
        public Limiter(float ceilingDb, float releaseSeconds, float sampleRate)
        {
            _ceiling = DspMath.DbToGain(ceilingDb);
            _release = DspMath.SmoothCoef(releaseSeconds, sampleRate);
        }

        /// <summary>Current gain (1 = no reduction).</summary>
        public float Gain => _gain;

        public void Process(float[] left, float[] right, int start, int count)
        {
            float g = _gain;
            float c = _ceiling;
            float rel = _release;
            int end = start + count;
            for (int i = start; i < end; i++)
            {
                float l = left[i];
                float r = right[i];
                float al = l < 0f ? -l : l;
                float ar = r < 0f ? -r : r;
                float peak = al > ar ? al : ar;
                g += rel * (1f - g);
                if (peak * g > c)
                {
                    g = c / peak;
                }

                left[i] = l * g;
                right[i] = r * g;
            }

            _gain = g;
        }

        /// <summary>
        /// Limits a seamless loop: first runs over the loop's last <paramref name="warmupFrames"/> to settle the
        /// gain state as it would be when the loop wraps, then processes the whole loop in place.
        /// </summary>
        public void ProcessLoop(float[] left, float[] right, int loopFrames, int warmupFrames)
        {
            if (warmupFrames > loopFrames)
            {
                warmupFrames = loopFrames;
            }

            float g = _gain;
            float c = _ceiling;
            float rel = _release;
            for (int i = loopFrames - warmupFrames; i < loopFrames; i++)
            {
                float al = left[i] < 0f ? -left[i] : left[i];
                float ar = right[i] < 0f ? -right[i] : right[i];
                float peak = al > ar ? al : ar;
                g += rel * (1f - g);
                if (peak * g > c)
                {
                    g = c / peak;
                }
            }

            _gain = g;
            Process(left, right, 0, loopFrames);
        }
    }
}

namespace BeamRunner.Core.Audio
{
    /// <summary>
    /// Phase-accumulator oscillator. Each call returns one sample and advances the phase by
    /// <c>inc = frequency / sampleRate</c> (must be in [0, 0.5)). Saw and pulse are band-limited with
    /// PolyBLEP; the triangle is naive (its harmonics fall at 12 dB/oct so aliasing is negligible).
    /// Mutable struct: keep it in a local or a field and call through it directly (never a copy).
    /// </summary>
    public struct Osc
    {
        /// <summary>Current phase in [0, 1).</summary>
        public float Phase;

        public Osc(float phase)
        {
            Phase = DspMath.Wrap01(phase < 0f ? phase + 1f : phase);
        }

        private void Advance(float inc)
        {
            float t = Phase + inc;
            if (t >= 1f)
            {
                t -= 1f;
            }

            Phase = t;
        }

        /// <summary>Band-limited sawtooth in [−1, 1].</summary>
        public float Saw(float inc)
        {
            float t = Phase;
            float v = 2f * t - 1f - DspMath.PolyBlep(t, inc);
            Advance(inc);
            return v;
        }

        /// <summary>Band-limited pulse with duty cycle <paramref name="width"/> (0.5 = square), DC-free.</summary>
        public float Pulse(float inc, float width)
        {
            float t = Phase;
            float v = t < width ? 1f : -1f;
            v += DspMath.PolyBlep(t, inc);
            float t2 = t - width;
            if (t2 < 0f)
            {
                t2 += 1f;
            }

            v -= DspMath.PolyBlep(t2, inc);
            Advance(inc);
            return v - (2f * width - 1f);
        }

        /// <summary>Band-limited square wave.</summary>
        public float Square(float inc)
        {
            return Pulse(inc, 0.5f);
        }

        /// <summary>Triangle wave in [−1, 1].</summary>
        public float Triangle(float inc)
        {
            float t = Phase;
            float v = t < 0.5f ? 4f * t - 1f : 3f - 4f * t;
            Advance(inc);
            return v;
        }

        /// <summary>Sine wave.</summary>
        public float Sine(float inc)
        {
            float v = DspMath.Sin01(Phase);
            Advance(inc);
            return v;
        }

        /// <summary>Sine with an added phase offset in cycles (for FM/PM synthesis); offset must be &gt; −16.</summary>
        public float SinePm(float inc, float phaseOffset)
        {
            float p = Phase + phaseOffset + 16f;
            p -= (int)p;
            float v = DspMath.Sin01(p);
            Advance(inc);
            return v;
        }
    }
}

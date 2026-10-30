namespace BeamRunner.Core.Audio
{
    /// <summary>
    /// Topology-preserving-transform state-variable filter (Zavalishin / Cytomic). Stable under fast
    /// per-sample cutoff modulation and produces low/band/high-pass simultaneously.
    /// Mutable struct — keep it in a local or field.
    /// </summary>
    public struct Svf
    {
        private float _ic1;
        private float _ic2;
        private float _a1;
        private float _a2;
        private float _a3;
        private float _k;

        /// <summary>Outputs of the most recent <see cref="Process"/> call.</summary>
        public float Low;
        public float Band;
        public float High;

        /// <summary>Sets cutoff (Hz, clamped to a safe range) and resonance Q (0.5 = soft, 0.707 = Butterworth, 5+ = peaky).</summary>
        public void Set(float cutoffHz, float q, float sampleRate)
        {
            float maxHz = sampleRate * 0.45f;
            if (cutoffHz < 10f)
            {
                cutoffHz = 10f;
            }
            else if (cutoffHz > maxHz)
            {
                cutoffHz = maxHz;
            }

            float g = DspMath.Tan(DspMath.Pi * cutoffHz / sampleRate);
            _k = 1f / (q < 0.1f ? 0.1f : q);
            _a1 = 1f / (1f + g * (g + _k));
            _a2 = g * _a1;
            _a3 = g * _a2;
        }

        /// <summary>Runs one sample; returns the low-pass output and fills <see cref="Low"/>, <see cref="Band"/>, <see cref="High"/>.</summary>
        public float Process(float v0)
        {
            float v3 = v0 - _ic2;
            float v1 = _a1 * _ic1 + _a2 * v3;
            float v2 = _ic2 + _a2 * _ic1 + _a3 * v3;
            _ic1 = 2f * v1 - _ic1;
            _ic2 = 2f * v2 - _ic2;
            Low = v2;
            Band = v1;
            High = v0 - _k * v1 - v2;
            return v2;
        }

        /// <summary>Low-pass only (slightly cheaper).</summary>
        public float LowPass(float v0)
        {
            float v3 = v0 - _ic2;
            float v1 = _a1 * _ic1 + _a2 * v3;
            float v2 = _ic2 + _a2 * _ic1 + _a3 * v3;
            _ic1 = 2f * v1 - _ic1;
            _ic2 = 2f * v2 - _ic2;
            return v2;
        }

        /// <summary>Band-pass output normalised to unity gain at the centre frequency.</summary>
        public float BandPass(float v0)
        {
            Process(v0);
            return Band * _k;
        }

        /// <summary>High-pass only.</summary>
        public float HighPass(float v0)
        {
            Process(v0);
            return High;
        }

        public void Reset()
        {
            _ic1 = 0f;
            _ic2 = 0f;
        }
    }
}

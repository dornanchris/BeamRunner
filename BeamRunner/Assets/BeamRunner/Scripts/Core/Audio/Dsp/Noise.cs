namespace BeamRunner.Core.Audio
{
    /// <summary>
    /// Deterministic noise source (xorshift32). <see cref="White"/> is uniform in [−1, 1);
    /// <see cref="Pink"/> uses Paul Kellet's economy filter (≈ −3 dB/oct, roughly unit RMS-matched).
    /// Mutable struct — keep it in a local or field.
    /// </summary>
    public struct Noise
    {
        private uint _state;
        private float _b0;
        private float _b1;
        private float _b2;

        public Noise(uint seed)
        {
            // Scramble so that small consecutive seeds give unrelated streams.
            uint s;
            unchecked
            {
                s = seed * 0x9E3779B1u + 0x7F4A7C15u;
                s ^= s >> 15;
                s *= 0x2C1B3C6Du;
                s ^= s >> 12;
            }

            _state = s == 0u ? 0x6D2B79F5u : s;
            _b0 = 0f;
            _b1 = 0f;
            _b2 = 0f;
        }

        public Noise(Rng rng) : this(rng.NextUInt())
        {
        }

        /// <summary>Uniform white noise in [−1, 1).</summary>
        public float White()
        {
            uint x = _state;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            _state = x;
            return unchecked((int)x) * (1f / 2147483648f);
        }

        /// <summary>Uniform value in [0, 1).</summary>
        public float Value()
        {
            return White() * 0.5f + 0.5f;
        }

        /// <summary>Pink-ish noise, scaled to roughly the same loudness as <see cref="White"/>.</summary>
        public float Pink()
        {
            float w = White();
            _b0 = 0.99765f * _b0 + w * 0.0990460f;
            _b1 = 0.96300f * _b1 + w * 0.2965164f;
            _b2 = 0.57000f * _b2 + w * 1.0526913f;
            return (_b0 + _b1 + _b2 + w * 0.1848f) * 0.22f;
        }
    }
}

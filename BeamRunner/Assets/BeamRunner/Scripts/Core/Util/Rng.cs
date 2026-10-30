using System;

namespace BeamRunner.Core
{
    /// <summary>
    /// Small, fast, deterministic PRNG (xoshiro128**). Same seed ⇒ same sequence on every platform,
    /// which keeps track generation reproducible for tests and bug reports.
    /// </summary>
    public sealed class Rng
    {
        private uint _s0;
        private uint _s1;
        private uint _s2;
        private uint _s3;

        public Rng(int seed)
        {
            // SplitMix32-style seeding so that nearby seeds produce unrelated streams.
            uint x = unchecked((uint)seed) + 0x9E3779B9u;
            _s0 = SplitMix(ref x);
            _s1 = SplitMix(ref x);
            _s2 = SplitMix(ref x);
            _s3 = SplitMix(ref x);
            if ((_s0 | _s1 | _s2 | _s3) == 0)
            {
                _s0 = 1;
            }
        }

        private static uint SplitMix(ref uint x)
        {
            unchecked
            {
                x += 0x9E3779B9u;
                uint z = x;
                z = (z ^ (z >> 16)) * 0x85EBCA6Bu;
                z = (z ^ (z >> 13)) * 0xC2B2AE35u;
                return z ^ (z >> 16);
            }
        }

        private static uint Rotl(uint x, int k)
        {
            return (x << k) | (x >> (32 - k));
        }

        /// <summary>Next raw 32-bit value.</summary>
        public uint NextUInt()
        {
            unchecked
            {
                uint result = Rotl(_s1 * 5u, 7) * 9u;
                uint t = _s1 << 9;
                _s2 ^= _s0;
                _s3 ^= _s1;
                _s1 ^= _s2;
                _s0 ^= _s3;
                _s2 ^= t;
                _s3 = Rotl(_s3, 11);
                return result;
            }
        }

        /// <summary>Uniform float in [0, 1).</summary>
        public float Value()
        {
            return (NextUInt() >> 8) * (1f / 16777216f);
        }

        /// <summary>Uniform float in [min, max).</summary>
        public float Range(float min, float max)
        {
            return min + (max - min) * Value();
        }

        /// <summary>Uniform int in [min, maxExclusive).</summary>
        public int Range(int min, int maxExclusive)
        {
            if (maxExclusive <= min)
            {
                return min;
            }

            return min + (int)(NextUInt() % (uint)(maxExclusive - min));
        }

        public bool Chance(float probability)
        {
            return Value() < probability;
        }

        /// <summary>−1 or +1.</summary>
        public int Sign()
        {
            return (NextUInt() & 1u) == 0 ? -1 : 1;
        }

        /// <summary>Uniform angle in [0, 2π).</summary>
        public float Angle()
        {
            return Value() * MathUtil.TwoPi;
        }

        /// <summary>Picks an index using non-negative weights. Returns 0 if all weights are zero.</summary>
        public int Weighted(float[] weights)
        {
            if (weights == null || weights.Length == 0)
            {
                throw new ArgumentException("weights must not be empty");
            }

            float total = 0f;
            for (int i = 0; i < weights.Length; i++)
            {
                total += Math.Max(0f, weights[i]);
            }

            if (total <= 0f)
            {
                return 0;
            }

            float r = Value() * total;
            for (int i = 0; i < weights.Length; i++)
            {
                float w = Math.Max(0f, weights[i]);
                if (r < w)
                {
                    return i;
                }

                r -= w;
            }

            return weights.Length - 1;
        }
    }
}

using System;
using Unity.IL2CPP.CompilerServices;

namespace BeamRunner.Core.Audio
{
    /// <summary>
    /// RBJ-cookbook biquad (transposed direct form II) for static EQ: low/high-pass, band-pass,
    /// peaking and shelving. Coefficients are computed in double precision.
    /// Mutable struct — keep it in a local or field.
    /// </summary>
    [Il2CppSetOption(Option.NullChecks, false)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
    public struct Biquad
    {
        private float _b0;
        private float _b1;
        private float _b2;
        private float _a1;
        private float _a2;
        private float _z1;
        private float _z2;

        public float Process(float x)
        {
            x += DspMath.AntiDenormal;
            float y = _b0 * x + _z1;
            _z1 = _b1 * x - _a1 * y + _z2;
            _z2 = _b2 * x - _a2 * y;
            return y;
        }

        /// <summary>Filters a buffer range in place.</summary>
        public void ProcessInPlace(float[] buffer, int start, int count)
        {
            int end = start + count;
            float b0 = _b0, b1 = _b1, b2 = _b2, a1 = _a1, a2 = _a2;
            float z1 = _z1, z2 = _z2;
            for (int i = start; i < end; i++)
            {
                // The offset keeps the state out of the (slow) denormal range during silent stretches.
                float x = buffer[i] + DspMath.AntiDenormal;
                float y = b0 * x + z1;
                z1 = b1 * x - a1 * y + z2;
                z2 = b2 * x - a2 * y;
                buffer[i] = y;
            }

            _z1 = z1;
            _z2 = z2;
        }

        public void Reset()
        {
            _z1 = 0f;
            _z2 = 0f;
        }

        private static Biquad Make(double b0, double b1, double b2, double a0, double a1, double a2)
        {
            var q = new Biquad();
            q._b0 = (float)(b0 / a0);
            q._b1 = (float)(b1 / a0);
            q._b2 = (float)(b2 / a0);
            q._a1 = (float)(a1 / a0);
            q._a2 = (float)(a2 / a0);
            return q;
        }

        private static void Prep(double hz, double q, double sampleRate, out double cosw, out double alpha)
        {
            hz = Math.Max(5.0, Math.Min(hz, sampleRate * 0.49));
            double w = 2.0 * Math.PI * hz / sampleRate;
            cosw = Math.Cos(w);
            alpha = Math.Sin(w) / (2.0 * Math.Max(0.05, q));
        }

        public static Biquad LowPass(double hz, double q, double sampleRate)
        {
            Prep(hz, q, sampleRate, out double c, out double a);
            return Make((1 - c) / 2, 1 - c, (1 - c) / 2, 1 + a, -2 * c, 1 - a);
        }

        public static Biquad HighPass(double hz, double q, double sampleRate)
        {
            Prep(hz, q, sampleRate, out double c, out double a);
            return Make((1 + c) / 2, -(1 + c), (1 + c) / 2, 1 + a, -2 * c, 1 - a);
        }

        /// <summary>Band-pass with 0 dB peak gain.</summary>
        public static Biquad BandPass(double hz, double q, double sampleRate)
        {
            Prep(hz, q, sampleRate, out double c, out double a);
            return Make(a, 0, -a, 1 + a, -2 * c, 1 - a);
        }

        public static Biquad Peak(double hz, double q, double gainDb, double sampleRate)
        {
            Prep(hz, q, sampleRate, out double c, out double a);
            double A = Math.Pow(10.0, gainDb / 40.0);
            return Make(1 + a * A, -2 * c, 1 - a * A, 1 + a / A, -2 * c, 1 - a / A);
        }

        public static Biquad LowShelf(double hz, double gainDb, double sampleRate)
        {
            return Shelf(hz, gainDb, sampleRate, false);
        }

        public static Biquad HighShelf(double hz, double gainDb, double sampleRate)
        {
            return Shelf(hz, gainDb, sampleRate, true);
        }

        private static Biquad Shelf(double hz, double gainDb, double sampleRate, bool high)
        {
            // Shelf slope S = 1.
            hz = Math.Max(5.0, Math.Min(hz, sampleRate * 0.49));
            double A = Math.Pow(10.0, gainDb / 40.0);
            double w = 2.0 * Math.PI * hz / sampleRate;
            double c = Math.Cos(w);
            double alpha = Math.Sin(w) / 2.0 * Math.Sqrt(2.0);
            double sq = 2.0 * Math.Sqrt(A) * alpha;
            if (high)
            {
                return Make(
                    A * ((A + 1) + (A - 1) * c + sq),
                    -2 * A * ((A - 1) + (A + 1) * c),
                    A * ((A + 1) + (A - 1) * c - sq),
                    (A + 1) - (A - 1) * c + sq,
                    2 * ((A - 1) - (A + 1) * c),
                    (A + 1) - (A - 1) * c - sq);
            }

            return Make(
                A * ((A + 1) - (A - 1) * c + sq),
                2 * A * ((A - 1) - (A + 1) * c),
                A * ((A + 1) - (A - 1) * c - sq),
                (A + 1) + (A - 1) * c + sq,
                -2 * ((A - 1) + (A + 1) * c),
                (A + 1) + (A - 1) * c - sq);
        }
    }
}

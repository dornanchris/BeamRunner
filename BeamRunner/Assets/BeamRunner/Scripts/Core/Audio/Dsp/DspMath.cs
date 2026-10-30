using System;
using Unity.IL2CPP.CompilerServices;

namespace BeamRunner.Core.Audio
{
    /// <summary>
    /// Shared DSP helpers: a sine lookup table, PolyBLEP residual, fast approximations of tan/tanh/exp,
    /// unit conversions and panning. Pure, allocation-free and thread-safe (the table is built once in
    /// the static constructor and only read afterwards).
    /// </summary>
    [Il2CppSetOption(Option.NullChecks, false)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
    public static class DspMath
    {
        public const float Pi = 3.14159265358979f;
        public const float TwoPi = 6.28318530717959f;

        /// <summary>
        /// Tiny DC offset injected into recursive structures (reverb, delay feedback) so their state never decays
        /// into denormal floats, which are ~100× slower on x86. −360 dBFS: inaudible and removed at the end.
        /// </summary>
        public const float AntiDenormal = 1e-18f;

        private const int SineBits = 12;
        private const int SineSize = 1 << SineBits;
        private const int SineMask = SineSize - 1;
        private static readonly float[] SineTable = BuildSine();

        private static float[] BuildSine()
        {
            var t = new float[SineSize + 1];
            for (int i = 0; i <= SineSize; i++)
            {
                t[i] = (float)Math.Sin(i * (2.0 * Math.PI / SineSize));
            }

            return t;
        }

        /// <summary>sin(2π·phase) via a linearly interpolated table. <paramref name="phase"/> must be ≥ 0.</summary>
        public static float Sin01(float phase)
        {
            float p = phase * SineSize;
            int i = (int)p;
            float f = p - i;
            i &= SineMask;
            float a = SineTable[i];
            return a + (SineTable[i + 1] - a) * f;
        }

        /// <summary>cos(2π·phase) via the sine table. <paramref name="phase"/> must be ≥ 0.</summary>
        public static float Cos01(float phase)
        {
            return Sin01(phase + 0.25f);
        }

        /// <summary>Wraps a non-negative phase into [0, 1).</summary>
        public static float Wrap01(float phase)
        {
            return phase - (int)phase;
        }

        /// <summary>
        /// PolyBLEP residual for a discontinuity at phase 0 of an oscillator with phase increment
        /// <paramref name="dt"/>. Subtract from a naive saw, add/subtract at the edges of a pulse.
        /// </summary>
        public static float PolyBlep(float t, float dt)
        {
            if (t < dt)
            {
                t /= dt;
                return t + t - t * t - 1f;
            }

            if (t > 1f - dt)
            {
                t = (t - 1f) / dt;
                return t * t + t + t + 1f;
            }

            return 0f;
        }

        /// <summary>Padé approximation of tan(x), accurate to ~0.1% on [0, 1.45].</summary>
        public static float Tan(float x)
        {
            float x2 = x * x;
            return x * (945f - 105f * x2 + x2 * x2) / (945f - 420f * x2 + 15f * x2 * x2);
        }

        /// <summary>Smooth saturator: rational tanh approximation, exact ±1 beyond |x| ≥ 3.</summary>
        public static float Tanh(float x)
        {
            if (x > 3f)
            {
                return 1f;
            }

            if (x < -3f)
            {
                return -1f;
            }

            float x2 = x * x;
            return x * (27f + x2) / (27f + 9f * x2);
        }

        /// <summary>
        /// Soft clipper with a linear region up to <paramref name="knee"/> and a smooth tanh-shaped
        /// shoulder that approaches (but never exceeds) 1.0.
        /// </summary>
        public static float SoftClip(float x, float knee)
        {
            float a = x < 0f ? -x : x;
            if (a <= knee)
            {
                return x;
            }

            float room = 1f - knee;
            float y = knee + room * Tanh((a - knee) / room);
            return x < 0f ? -y : y;
        }

        public static float DbToGain(float db)
        {
            return (float)Math.Pow(10.0, db / 20.0);
        }

        public static float GainToDb(float gain)
        {
            return gain <= 1e-10f ? -200f : (float)(20.0 * Math.Log10(gain));
        }

        /// <summary>MIDI note number (69 = A4 = 440 Hz) to frequency in Hz.</summary>
        public static float MidiToHz(float midi)
        {
            return (float)(440.0 * Math.Pow(2.0, (midi - 69.0) / 12.0));
        }

        /// <summary>Frequency ratio for a detune in cents.</summary>
        public static float Cents(float cents)
        {
            return (float)Math.Pow(2.0, cents / 1200.0);
        }

        /// <summary>
        /// Per-sample multiplier that makes a value decay by 60 dB over <paramref name="seconds"/>.
        /// </summary>
        public static float DecayCoef(float seconds, float sampleRate)
        {
            if (seconds <= 0f)
            {
                return 0f;
            }

            return (float)Math.Exp(-6.907755 / (seconds * sampleRate));
        }

        /// <summary>One-pole smoothing coefficient for a time constant in seconds (reaches 63% after it).</summary>
        public static float SmoothCoef(float seconds, float sampleRate)
        {
            if (seconds <= 0f)
            {
                return 1f;
            }

            return 1f - (float)Math.Exp(-1.0 / (seconds * sampleRate));
        }

        /// <summary>Balance-style panning gains with unity centre: pan −1 = hard left, +1 = hard right.</summary>
        public static void Pan(float pan, out float left, out float right)
        {
            if (pan < -1f)
            {
                pan = -1f;
            }
            else if (pan > 1f)
            {
                pan = 1f;
            }

            left = pan > 0f ? 1f - pan : 1f;
            right = pan < 0f ? 1f + pan : 1f;
        }

        /// <summary>Hermite smoothstep on [0, 1].</summary>
        public static float Smooth(float t)
        {
            if (t <= 0f)
            {
                return 0f;
            }

            if (t >= 1f)
            {
                return 1f;
            }

            return t * t * (3f - 2f * t);
        }

        public static float Clamp(float v, float min, float max)
        {
            return v < min ? min : (v > max ? max : v);
        }

        public static float Lerp(float a, float b, float t)
        {
            return a + (b - a) * t;
        }
    }
}

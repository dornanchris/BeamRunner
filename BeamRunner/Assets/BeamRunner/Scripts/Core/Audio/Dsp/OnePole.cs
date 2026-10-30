using System;

namespace BeamRunner.Core.Audio
{
    /// <summary>
    /// One-pole low-pass / smoother with a matching high-pass output (x − lowpass). Also used as a
    /// DC blocker and for parameter smoothing. Mutable struct — keep it in a local or field.
    /// </summary>
    public struct OnePole
    {
        private float _a;

        /// <summary>Current (low-pass) state.</summary>
        public float Value;

        public static OnePole FromHz(float cutoffHz, float sampleRate)
        {
            var p = new OnePole();
            p.SetHz(cutoffHz, sampleRate);
            return p;
        }

        public static OnePole FromSeconds(float seconds, float sampleRate)
        {
            var p = new OnePole();
            p._a = DspMath.SmoothCoef(seconds, sampleRate);
            return p;
        }

        public void SetHz(float cutoffHz, float sampleRate)
        {
            _a = 1f - (float)Math.Exp(-2.0 * Math.PI * cutoffHz / sampleRate);
        }

        public float LowPass(float x)
        {
            Value += _a * (x - Value);
            return Value;
        }

        public float HighPass(float x)
        {
            Value += _a * (x - Value);
            return x - Value;
        }
    }
}

using Unity.IL2CPP.CompilerServices;

namespace BeamRunner.Core.Audio
{
    /// <summary>
    /// Stereo chorus: one modulated delay tap per side with the LFOs 90° apart, fed from the mono sum.
    /// Adds only the wet signal to the outputs. For seamless loops choose a rate that completes an integer
    /// number of cycles per loop (see <see cref="MusicClock.LoopRate"/>).
    /// </summary>
    [Il2CppSetOption(Option.NullChecks, false)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
    public sealed class Chorus
    {
        private readonly float[] _buf;
        private readonly int _mask;
        private readonly float _base;
        private readonly float _depth;
        private readonly float _inc;
        private float _phase;
        private int _pos;

        public Chorus(float sampleRate, float baseDelayMs, float depthMs, float rateHz)
        {
            _base = baseDelayMs * 0.001f * sampleRate;
            _depth = depthMs * 0.001f * sampleRate;
            int need = (int)(_base + _depth) + 4;
            int size = 1;
            while (size < need)
            {
                size <<= 1;
            }

            _buf = new float[size];
            _mask = size - 1;
            _inc = rateHz / sampleRate;
        }

        public void Process(float[] inL, float[] inR, float[] outL, float[] outR, int start, int count, float wet)
        {
            float[] buf = _buf;
            int mask = _mask;
            int pos = _pos;
            float phase = _phase;
            int end = start + count;
            float dl = 0f;
            float dr = 0f;
            float stepL = 0f;
            float stepR = 0f;
            for (int i = start; i < end; i++)
            {
                // The LFO is evaluated every 16 samples and the delay times are ramped linearly in between.
                if (((i - start) & 15) == 0)
                {
                    float nextPhase = phase + 16f * _inc;
                    float tl = _base + _depth * (0.5f + 0.5f * DspMath.Sin01(nextPhase - (int)nextPhase));
                    float tr = _base + _depth * (0.5f + 0.5f * DspMath.Sin01(DspMath.Wrap01(nextPhase + 0.25f)));
                    dl = _base + _depth * (0.5f + 0.5f * DspMath.Sin01(phase));
                    dr = _base + _depth * (0.5f + 0.5f * DspMath.Sin01(DspMath.Wrap01(phase + 0.25f)));
                    stepL = (tl - dl) * (1f / 16f);
                    stepR = (tr - dr) * (1f / 16f);
                }

                buf[pos] = (inL[i] + inR[i]) * 0.5f;
                outL[i] += Read(buf, mask, pos, dl) * wet;
                outR[i] += Read(buf, mask, pos, dr) * wet;
                dl += stepL;
                dr += stepR;
                pos = (pos + 1) & mask;
                phase += _inc;
                if (phase >= 1f)
                {
                    phase -= 1f;
                }
            }

            _pos = pos;
            _phase = phase;
        }

        private static float Read(float[] buf, int mask, int pos, float delay)
        {
            int di = (int)delay;
            float f = delay - di;
            float a = buf[(pos - di) & mask];
            float b = buf[(pos - di - 1) & mask];
            return a + (b - a) * f;
        }
    }
}

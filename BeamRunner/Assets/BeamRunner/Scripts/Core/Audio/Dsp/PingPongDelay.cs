using Unity.IL2CPP.CompilerServices;

namespace BeamRunner.Core.Audio
{
    /// <summary>
    /// Stereo ping-pong delay (first echo left, then right, …) with low- and high-pass damping in the
    /// feedback path so repeats get darker and thinner. Linear and time-invariant, so it folds
    /// cleanly into seamless loops.
    /// </summary>
    [Il2CppSetOption(Option.NullChecks, false)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
    public sealed class PingPongDelay
    {
        private readonly float[] _left;
        private readonly float[] _right;
        private readonly int _mask;
        private readonly int _delay;
        private readonly float _feedback;
        private int _pos;
        private OnePole _lpL;
        private OnePole _lpR;
        private OnePole _hpL;
        private OnePole _hpR;

        /// <param name="delayFrames">Delay per side in frames (e.g. a dotted eighth).</param>
        /// <param name="feedback">0..0.9 amount fed back per repeat.</param>
        /// <param name="lowPassHz">Damping of the repeats.</param>
        /// <param name="highPassHz">Low cut of the repeats (keeps the mix clean).</param>
        public PingPongDelay(int delayFrames, float feedback, float lowPassHz, float highPassHz, float sampleRate)
        {
            if (delayFrames < 1)
            {
                delayFrames = 1;
            }

            int size = 1;
            while (size <= delayFrames + 1)
            {
                size <<= 1;
            }

            _left = new float[size];
            _right = new float[size];
            _mask = size - 1;
            _delay = delayFrames;
            _feedback = feedback;
            _lpL = OnePole.FromHz(lowPassHz, sampleRate);
            _lpR = OnePole.FromHz(lowPassHz, sampleRate);
            _hpL = OnePole.FromHz(highPassHz, sampleRate);
            _hpR = OnePole.FromHz(highPassHz, sampleRate);
        }

        /// <summary>
        /// Reads <paramref name="inL"/>/<paramref name="inR"/> (scaled by <paramref name="send"/>) and adds the
        /// wet signal (scaled by <paramref name="wet"/>) into <paramref name="outL"/>/<paramref name="outR"/>.
        /// Input and output arrays may be the same.
        /// </summary>
        public void Process(float[] inL, float[] inR, float[] outL, float[] outR, int start, int count, float send, float wet)
        {
            float[] bl = _left;
            float[] br = _right;
            int mask = _mask;
            int pos = _pos;
            int d = _delay;
            float fb = _feedback;
            int end = start + count;
            for (int i = start; i < end; i++)
            {
                int rd = (pos - d) & mask;
                float yl = bl[rd];
                float yr = br[rd];
                float x = (inL[i] + inR[i]) * 0.5f * send;

                // Damp each repeat: low-pass then remove lows.
                float fl = _hpL.HighPass(_lpL.LowPass(yl));
                float fr = _hpR.HighPass(_lpR.LowPass(yr));
                bl[pos] = x + fr * fb + DspMath.AntiDenormal;
                br[pos] = fl * fb + DspMath.AntiDenormal;
                outL[i] += yl * wet;
                outR[i] += yr * wet;
                pos = (pos + 1) & mask;
            }

            _pos = pos;
        }
    }
}

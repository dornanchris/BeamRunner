using System;
using Unity.IL2CPP.CompilerServices;

namespace BeamRunner.Core.Audio
{
    /// <summary>
    /// Light stereo reverb: pre-delay → 4 series all-pass diffusers → 8-line feedback delay network with an
    /// orthogonal (Walsh–Hadamard) mixing matrix and per-line high-frequency damping. No modulation, so it is
    /// linear and time-invariant (loop tails fold back exactly).
    /// <para>
    /// For speed the network runs at half the sample rate when the rate is ≥ 30 kHz (the tail is damped well
    /// below the reduced Nyquist anyway): the mono input is decimated with a [¼ ½ ¼] kernel and the output is
    /// linearly interpolated back. Processing is block-based with fixed scratch buffers (no per-call allocation).
    /// </para>
    /// </summary>
    [Il2CppSetOption(Option.NullChecks, false)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
    public sealed class Reverb
    {
        private const int Lines = 8;
        private const int LineSize = 4096;
        private const int LineMask = LineSize - 1;
        private const int Block = 2048;

        // Mutually-prime-ish base lengths in milliseconds (scaled by room size).
        private static readonly double[] BaseMs = { 29.7, 35.3, 41.1, 44.3, 49.9, 55.1, 60.7, 68.3 };
        private static readonly double[] DiffuserMs = { 4.77, 3.59, 12.73, 9.30 };

        private readonly int _factor;
        private readonly float[] _lines = new float[Lines * LineSize];
        private readonly int[] _len = new int[Lines];
        private readonly float[] _gain = new float[Lines];
        private readonly float[] _damp = new float[Lines];
        private readonly float[][] _ap = new float[4][];
        private readonly int[] _apPos = new int[4];
        private readonly float[] _pre;
        private readonly int _preLen;
        private readonly float _dampCoef;
        private readonly float _inputHpCoef;
        private readonly float[] _mono = new float[Block];
        private readonly float[] _yl = new float[Block];
        private readonly float[] _yr = new float[Block];
        private int _prePos;
        private int _pos;
        private float _hpState;
        private float _prevIn;
        private float _prevL;
        private float _prevR;

        /// <param name="sampleRate">Stream sample rate.</param>
        /// <param name="size">Room size scale, ~0.5 (small room) .. 1.6 (big hall).</param>
        /// <param name="rt60">Decay time in seconds (−60 dB).</param>
        /// <param name="dampHz">High-frequency damping cutoff inside the loop.</param>
        /// <param name="preDelayMs">Pre-delay in milliseconds.</param>
        public Reverb(float sampleRate, float size, float rt60, float dampHz, float preDelayMs)
        {
            _factor = sampleRate >= 30000f ? 2 : 1;
            double sr = sampleRate / _factor;
            for (int i = 0; i < Lines; i++)
            {
                int len = (int)(BaseMs[i] * 0.001 * sr * size) | 1;
                len = Math.Max(32, Math.Min(LineSize - 1, len));
                _len[i] = len;
                _gain[i] = (float)Math.Pow(10.0, -3.0 * len / (rt60 * sr));
            }

            for (int i = 0; i < 4; i++)
            {
                int len = Math.Max(4, (int)(DiffuserMs[i] * 0.001 * sr * Math.Min(1.0, 0.6 + 0.4 * size)));
                _ap[i] = new float[len];
            }

            _preLen = Math.Max(1, (int)(preDelayMs * 0.001 * sr));
            _pre = new float[_preLen];
            _dampCoef = 1f - (float)Math.Exp(-2.0 * Math.PI * Math.Min(dampHz, sr * 0.45) / sr);
            _inputHpCoef = 1f - (float)Math.Exp(-2.0 * Math.PI * 180.0 / sr);
        }

        /// <summary>
        /// Adds the reverb of (<paramref name="inL"/>+<paramref name="inR"/>)/2·<paramref name="send"/> into the
        /// outputs, scaled by <paramref name="wet"/>. Input and output arrays may be the same.
        /// </summary>
        public void Process(float[] inL, float[] inR, float[] outL, float[] outR, int start, int count, float send, float wet)
        {
            int end = start + count;
            float half = 0.5f * send;
            int blockFrames = Block * _factor;
            for (int b0 = start; b0 < end; b0 += blockFrames)
            {
                int n = Math.Min(blockFrames, end - b0);
                int m;
                if (_factor == 2)
                {
                    m = (n + 1) >> 1;
                    float a = _prevIn;
                    for (int k = 0; k < m; k++)
                    {
                        int i = b0 + 2 * k;
                        float x0 = (inL[i] + inR[i]) * half;
                        float x1 = i + 1 < end ? (inL[i + 1] + inR[i + 1]) * half : 0f;
                        _mono[k] = 0.25f * a + 0.5f * x0 + 0.25f * x1;
                        a = x1;
                    }

                    _prevIn = a;
                }
                else
                {
                    m = n;
                    for (int k = 0; k < m; k++)
                    {
                        _mono[k] = (inL[b0 + k] + inR[b0 + k]) * half;
                    }
                }

                Run(m);

                if (_factor == 2)
                {
                    float pl = _prevL;
                    float pr = _prevR;
                    for (int k = 0; k < m; k++)
                    {
                        int i = b0 + 2 * k;
                        float yl = _yl[k] * wet;
                        float yr = _yr[k] * wet;
                        outL[i] += 0.5f * (pl + yl);
                        outR[i] += 0.5f * (pr + yr);
                        if (i + 1 < end)
                        {
                            outL[i + 1] += yl;
                            outR[i + 1] += yr;
                        }

                        pl = yl;
                        pr = yr;
                    }

                    _prevL = pl;
                    _prevR = pr;
                }
                else
                {
                    for (int k = 0; k < m; k++)
                    {
                        outL[b0 + k] += _yl[k] * wet;
                        outR[b0 + k] += _yr[k] * wet;
                    }
                }
            }
        }

        /// <summary>Runs the network over <c>_mono[0..m)</c>, writing <c>_yl/_yr</c>.</summary>
        private void Run(int m)
        {
            float[] mono = _mono;
            float[] yL = _yl;
            float[] yR = _yr;
            float[] lines = _lines;
            float[] pre = _pre;
            float[] ap0 = _ap[0];
            float[] ap1 = _ap[1];
            float[] ap2 = _ap[2];
            float[] ap3 = _ap[3];
            int p0 = _apPos[0];
            int p1 = _apPos[1];
            int p2 = _apPos[2];
            int p3 = _apPos[3];
            int l0 = ap0.Length;
            int l1 = ap1.Length;
            int l2 = ap2.Length;
            int l3 = ap3.Length;
            int prePos = _prePos;
            int preLen = _preLen;
            int pos = _pos;
            float dc = _dampCoef;
            float hpC = _inputHpCoef;
            float hp = _hpState;

            int n0 = _len[0], n1 = _len[1], n2 = _len[2], n3 = _len[3];
            int n4 = _len[4], n5 = _len[5], n6 = _len[6], n7 = _len[7];
            const float H = 0.35355339f;
            float g0 = _gain[0] * H, g1 = _gain[1] * H, g2 = _gain[2] * H, g3 = _gain[3] * H;
            float g4 = _gain[4] * H, g5 = _gain[5] * H, g6 = _gain[6] * H, g7 = _gain[7] * H;
            float d0 = _damp[0], d1 = _damp[1], d2 = _damp[2], d3 = _damp[3];
            float d4 = _damp[4], d5 = _damp[5], d6 = _damp[6], d7 = _damp[7];
            const float ApG = 0.62f;

            for (int k = 0; k < m; k++)
            {
                // Input high-pass (keeps the tail from getting muddy); the offset keeps states out of denormals.
                float x = mono[k] + DspMath.AntiDenormal;
                hp += hpC * (x - hp);
                x = x - hp + DspMath.AntiDenormal;

                float pd = pre[prePos];
                pre[prePos] = x;
                if (++prePos >= preLen)
                {
                    prePos = 0;
                }

                // Series all-pass diffusion.
                float b = ap0[p0];
                float v = pd + ApG * b;
                ap0[p0] = v;
                pd = b - ApG * v;
                if (++p0 >= l0)
                {
                    p0 = 0;
                }

                b = ap1[p1];
                v = pd + ApG * b;
                ap1[p1] = v;
                pd = b - ApG * v;
                if (++p1 >= l1)
                {
                    p1 = 0;
                }

                b = ap2[p2];
                v = pd + ApG * b;
                ap2[p2] = v;
                pd = b - ApG * v;
                if (++p2 >= l2)
                {
                    p2 = 0;
                }

                b = ap3[p3];
                v = pd + ApG * b;
                ap3[p3] = v;
                pd = b - ApG * v;
                if (++p3 >= l3)
                {
                    p3 = 0;
                }

                // Read and damp the delay lines.
                d0 += dc * (lines[(pos - n0) & LineMask] - d0);
                d1 += dc * (lines[LineSize + ((pos - n1) & LineMask)] - d1);
                d2 += dc * (lines[2 * LineSize + ((pos - n2) & LineMask)] - d2);
                d3 += dc * (lines[3 * LineSize + ((pos - n3) & LineMask)] - d3);
                d4 += dc * (lines[4 * LineSize + ((pos - n4) & LineMask)] - d4);
                d5 += dc * (lines[5 * LineSize + ((pos - n5) & LineMask)] - d5);
                d6 += dc * (lines[6 * LineSize + ((pos - n6) & LineMask)] - d6);
                d7 += dc * (lines[7 * LineSize + ((pos - n7) & LineMask)] - d7);

                // Output taps (decorrelated sign patterns).
                yL[k] = (d0 - d2 + d4 - d6 + d1 * 0.5f) * 0.5f;
                yR[k] = (d1 - d3 + d5 - d7 + d2 * 0.5f) * 0.5f;

                // Fast Walsh–Hadamard mixing (normalisation H folded into the gains).
                float a0 = d0 + d1, a1 = d0 - d1, a2 = d2 + d3, a3 = d2 - d3;
                float a4 = d4 + d5, a5 = d4 - d5, a6 = d6 + d7, a7 = d6 - d7;
                float c0 = a0 + a2, c2 = a0 - a2, c1 = a1 + a3, c3 = a1 - a3;
                float c4 = a4 + a6, c6 = a4 - a6, c5 = a5 + a7, c7 = a5 - a7;

                lines[pos] = pd + g0 * (c0 + c4);
                lines[LineSize + pos] = -pd + g1 * (c1 + c5);
                lines[2 * LineSize + pos] = pd + g2 * (c2 + c6);
                lines[3 * LineSize + pos] = -pd + g3 * (c3 + c7);
                lines[4 * LineSize + pos] = pd + g4 * (c0 - c4);
                lines[5 * LineSize + pos] = -pd + g5 * (c1 - c5);
                lines[6 * LineSize + pos] = pd + g6 * (c2 - c6);
                lines[7 * LineSize + pos] = -pd + g7 * (c3 - c7);
                pos = (pos + 1) & LineMask;
            }

            _apPos[0] = p0;
            _apPos[1] = p1;
            _apPos[2] = p2;
            _apPos[3] = p3;
            _prePos = prePos;
            _pos = pos;
            _hpState = hp;
            _damp[0] = d0; _damp[1] = d1; _damp[2] = d2; _damp[3] = d3;
            _damp[4] = d4; _damp[5] = d5; _damp[6] = d6; _damp[7] = d7;
        }
    }
}

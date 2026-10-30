using System;
using BeamRunner.Core.Field;

namespace BeamRunner.Core.Sim
{
    /// <summary>
    /// A recorded run for ghost racing, parameterised by track distance instead of frames: the craft's X/Y position every
    /// <see cref="SampleSpacing"/> metres, plus a sparse time track (the run time every <see cref="TimeCheckpointEvery"/>
    /// samples) that says when the ghost passed each point. Independent of frame rate, pauses and device speed.
    /// Built by <see cref="GhostRecorder"/>; persisted with <see cref="Encode"/> / <see cref="Decode"/>.
    /// <para><b>Binary format</b> (little endian, then Base64 for the JSON save):</para>
    /// <code>
    /// [0]     format version (1)
    /// [1]     field generator version (DailyRuns.GeneratorVersion)
    /// [2..5]  field seed (int32)
    /// [6..7]  sample count N (2 … MaxSamples)
    /// N × 2   uint8 x, uint8 y: absolute positions across the field (PositionSteps steps from −HalfExtent to +HalfExtent)
    /// C ×     uint16 time: sample 0's time in ms, then ms since the previous checkpoint;
    ///         checkpoints at samples 0, K, 2K, … and N−1 (C = 1 + ceil((N−1)/K), K = TimeCheckpointEvery)
    /// </code>
    /// <para><b>Size.</b> 2 bytes per 2 m for the position + 2 bytes per 16 m for the time = 1.125 byte/m: a 10 km run is
    /// ~11 KB (15 KB of Base64 in the save), the 16.4 km cap 18 KB (25 KB of Base64). Only the day's best run is kept.</para>
    /// <para><b>Accuracy.</b> Absolute coding cannot drift; one step is 18 m / 255 ≈ 7 cm. Catmull-Rom interpolation
    /// between samples follows the craft's smooth steering closely (it moves at most ~1.3 m between samples).</para>
    /// </summary>
    public sealed class GhostPath
    {
        /// <summary>Distance between position samples (metres).</summary>
        public const float SampleSpacing = 2f;

        /// <summary>Position samples per time checkpoint (16 m).</summary>
        public const int TimeCheckpointEvery = 8;

        /// <summary>Highest quantised position value (positions use 0 … PositionSteps).</summary>
        public const int PositionSteps = 255;

        /// <summary>Sample cap: 8,192 samples = 16,382 m (longer runs keep their first 16.4 km).</summary>
        public const int MaxSamples = 8192;

        /// <summary>Current binary format version.</summary>
        public const byte FormatVersion = 1;

        private const int HeaderBytes = 8;

        /// <summary>Largest valid encoding (bytes).</summary>
        public static readonly int MaxEncodedBytes = EncodedBytes(MaxSamples);

        /// <summary>Largest valid encoding as Base64 text (what the save stores).</summary>
        public static readonly int MaxEncodedChars = 4 * ((MaxEncodedBytes + 2) / 3);

        private readonly float[] _x;
        private readonly float[] _y;
        private readonly int[] _checkpointMs;
        private readonly byte[] _bytes;

        private GhostPath(byte[] bytes, int seed, int trackVersion, float[] x, float[] y, int[] checkpointMs)
        {
            _bytes = bytes;
            Seed = seed;
            TrackVersion = trackVersion;
            _x = x;
            _y = y;
            _checkpointMs = checkpointMs;
        }

        /// <summary>Seed of the field the run was flown on.</summary>
        public int Seed { get; private set; }

        /// <summary>Generator version of the field the run was flown on.</summary>
        public int TrackVersion { get; private set; }

        /// <summary>Number of position samples (≥ 2).</summary>
        public int SampleCount
        {
            get { return _x.Length; }
        }

        /// <summary>Distance covered by the samples (metres from the start).</summary>
        public double Length
        {
            get { return (_x.Length - 1) * (double)SampleSpacing; }
        }

        /// <summary>Run time at <see cref="Length"/> (seconds).</summary>
        public double Duration
        {
            get { return _checkpointMs[_checkpointMs.Length - 1] / 1000.0; }
        }

        /// <summary>Size of the binary encoding (bytes).</summary>
        public int EncodedSize
        {
            get { return _bytes.Length; }
        }

        /// <summary>Bytes needed for a path of <paramref name="samples"/> samples.</summary>
        public static int EncodedBytes(int samples)
        {
            if (samples < 2)
            {
                return 0;
            }

            int intervals = samples - 1;
            int checkpoints = 1 + (intervals + TimeCheckpointEvery - 1) / TimeCheckpointEvery;
            return HeaderBytes + 2 * samples + 2 * checkpoints;
        }

        /// <summary>Quantises a field position (clamped to the field).</summary>
        public static int Quantise(float position)
        {
            double f = (position + FieldSpec.HalfExtent) / (2.0 * FieldSpec.HalfExtent);
            int q = (int)Math.Round(f * PositionSteps);
            return q < 0 ? 0 : (q > PositionSteps ? PositionSteps : q);
        }

        /// <summary>The field position a quantised value stands for.</summary>
        public static float Dequantise(int q)
        {
            return (float)(q / (double)PositionSteps * 2.0 * FieldSpec.HalfExtent - FieldSpec.HalfExtent);
        }

        // ------------------------------------------------------------------ queries

        /// <summary>
        /// Craft position at a track distance: Catmull-Rom through the samples, clamped to the first/last sample outside
        /// the path.
        /// </summary>
        public void PositionAt(double distance, out float x, out float y)
        {
            x = Interpolate(_x, distance);
            y = Interpolate(_y, distance);
        }

        private static float Interpolate(float[] v, double distance)
        {
            int last = v.Length - 1;
            double s = distance / SampleSpacing;
            if (!(s > 0.0))
            {
                return v[0];
            }

            if (s >= last)
            {
                return v[last];
            }

            int i = (int)s;
            float t = (float)(s - i);
            float p1 = v[i];
            float p2 = v[i + 1];
            float p0 = i > 0 ? v[i - 1] : 2f * p1 - p2;
            float p3 = i + 2 <= last ? v[i + 2] : 2f * p2 - p1;
            float t2 = t * t;
            float t3 = t2 * t;
            float r = 0.5f * (2f * p1 + (p2 - p0) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
                              (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
            return MathUtil.Clamp(r, -FieldSpec.HalfExtent, FieldSpec.HalfExtent);
        }

        /// <summary>Run time (seconds) at which the ghost passed <paramref name="distance"/>; clamped to the path.</summary>
        public double TimeAt(double distance)
        {
            int last = _x.Length - 1;
            double s = distance / SampleSpacing;
            if (!(s > 0.0))
            {
                return _checkpointMs[0] / 1000.0;
            }

            if (s >= last)
            {
                return Duration;
            }

            int segment = Math.Min((int)(s / TimeCheckpointEvery), _checkpointMs.Length - 2);
            int a = segment * TimeCheckpointEvery;
            int b = Math.Min(a + TimeCheckpointEvery, last);
            double f = (s - a) / (b - a);
            double ms = _checkpointMs[segment] + (_checkpointMs[segment + 1] - _checkpointMs[segment]) * f;
            return ms / 1000.0;
        }

        /// <summary>
        /// Where the ghost is at run time <paramref name="time"/> (metres from the start). 0 before its start,
        /// <see cref="Length"/> once it is finished (<see cref="IsFinished"/>).
        /// </summary>
        public double DistanceAt(double time)
        {
            double ms = time * 1000.0;
            int n = _checkpointMs.Length;
            if (!(ms > _checkpointMs[0]))
            {
                return 0.0;
            }

            if (ms >= _checkpointMs[n - 1])
            {
                return Length;
            }

            // Last checkpoint with time <= ms.
            int lo = 0;
            int hi = n - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) >> 1;
                if (_checkpointMs[mid] <= ms)
                {
                    lo = mid;
                }
                else
                {
                    hi = mid;
                }
            }

            int last = _x.Length - 1;
            int a = Math.Min(lo * TimeCheckpointEvery, last);
            int b = Math.Min(a + TimeCheckpointEvery, last);
            int span = _checkpointMs[lo + 1] - _checkpointMs[lo];
            double f = span > 0 ? (ms - _checkpointMs[lo]) / span : 1.0;
            return (a + (b - a) * f) * SampleSpacing;
        }

        /// <summary>True once run time <paramref name="time"/> is past the end of the recording (the run ended there).</summary>
        public bool IsFinished(double time)
        {
            return time * 1000.0 >= _checkpointMs[_checkpointMs.Length - 1];
        }

        /// <summary>The reconstructed (quantised) sample position <paramref name="index"/> (tests / tools).</summary>
        public float SampleX(int index)
        {
            return _x[index];
        }

        public float SampleY(int index)
        {
            return _y[index];
        }

        // ------------------------------------------------------------------ encoding

        /// <summary>A copy of the binary encoding.</summary>
        public byte[] ToBytes()
        {
            return (byte[])_bytes.Clone();
        }

        /// <summary>Base64 of the binary encoding (what the profile stores).</summary>
        public string Encode()
        {
            return Convert.ToBase64String(_bytes);
        }

        /// <summary>Parses <see cref="Encode"/> output; null for empty, damaged or unknown data (never throws).</summary>
        public static GhostPath Decode(string base64)
        {
            if (string.IsNullOrEmpty(base64) || base64.Length > MaxEncodedChars)
            {
                return null;
            }

            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(base64);
            }
            catch (FormatException)
            {
                return null;
            }

            return FromBytes(bytes);
        }

        /// <summary>Parses a binary encoding; null for damaged or unknown data (never throws).</summary>
        public static GhostPath FromBytes(byte[] bytes)
        {
            if (bytes == null || bytes.Length < HeaderBytes || bytes[0] != FormatVersion)
            {
                return null;
            }

            int trackVersion = bytes[1];
            int seed = bytes[2] | (bytes[3] << 8) | (bytes[4] << 16) | (bytes[5] << 24);
            int count = bytes[6] | (bytes[7] << 8);
            if (count < 2 || count > MaxSamples || bytes.Length != EncodedBytes(count))
            {
                return null;
            }

            var xs = new float[count];
            var ys = new float[count];
            int p = HeaderBytes;
            for (int i = 0; i < count; i++)
            {
                xs[i] = Dequantise(bytes[p++]);
                ys[i] = Dequantise(bytes[p++]);
            }

            int checkpoints = 1 + (count - 1 + TimeCheckpointEvery - 1) / TimeCheckpointEvery;
            var times = new int[checkpoints];
            int ms = 0;
            for (int j = 0; j < checkpoints; j++)
            {
                ms += bytes[p] | (bytes[p + 1] << 8);
                p += 2;
                times[j] = ms;
            }

            var copy = (byte[])bytes.Clone();
            return new GhostPath(copy, seed, trackVersion, xs, ys, times);
        }

        /// <summary>Builds a path from already-quantised data (used by <see cref="GhostRecorder"/>).</summary>
        internal static GhostPath Build(int seed, int trackVersion, byte[] positions, int count, int[] checkpointMs,
            int checkpointCount)
        {
            if (count < 2 || count > MaxSamples)
            {
                return null;
            }

            int expected = 1 + (count - 1 + TimeCheckpointEvery - 1) / TimeCheckpointEvery;
            if (checkpointCount != expected)
            {
                return null;
            }

            var bytes = new byte[EncodedBytes(count)];
            bytes[0] = FormatVersion;
            bytes[1] = (byte)MathUtil.Clamp(trackVersion, 0, 255);
            bytes[2] = (byte)seed;
            bytes[3] = (byte)(seed >> 8);
            bytes[4] = (byte)(seed >> 16);
            bytes[5] = (byte)(seed >> 24);
            bytes[6] = (byte)count;
            bytes[7] = (byte)(count >> 8);
            Array.Copy(positions, 0, bytes, HeaderBytes, 2 * count);
            int p = HeaderBytes + 2 * count;
            int previous = 0;
            for (int j = 0; j < checkpointCount; j++)
            {
                int delta = MathUtil.Clamp(checkpointMs[j] - previous, 0, ushort.MaxValue);
                previous += delta;
                bytes[p++] = (byte)delta;
                bytes[p++] = (byte)(delta >> 8);
            }

            return FromBytes(bytes);
        }
    }
}

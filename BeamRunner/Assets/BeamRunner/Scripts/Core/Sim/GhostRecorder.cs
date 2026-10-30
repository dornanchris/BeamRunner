using System;

namespace BeamRunner.Core.Sim
{
    /// <summary>
    /// Records a run into a <see cref="GhostPath"/>: feed it the simulation's (Distance, ShipX, ShipY, Time) once at the
    /// start (distance 0) and after every <see cref="RunSimulation.Step"/>. Samples are placed exactly every
    /// <see cref="GhostPath.SampleSpacing"/> metres by interpolating between observations, so the result does not depend on
    /// the frame rate. Quantises on the fly (2 bytes per sample), so memory stays at ~16 KB even for the longest run.
    /// </summary>
    public sealed class GhostRecorder
    {
        private readonly int _seed;
        private readonly int _trackVersion;
        private readonly byte[] _positions = new byte[GhostPath.MaxSamples * 2];
        private readonly int[] _checkpointMs = new int[2 + (GhostPath.MaxSamples - 1) / GhostPath.TimeCheckpointEvery];

        private int _count;
        private int _checkpointCount;
        private int _lastMs;

        private bool _hasPrevious;
        private double _prevDistance;
        private float _prevX;
        private float _prevY;
        private double _prevTime;

        /// <param name="seed">Seed of the field being flown (stored in the path).</param>
        /// <param name="trackVersion">Generator version of that field (stored in the path).</param>
        public GhostRecorder(int seed, int trackVersion)
        {
            _seed = seed;
            _trackVersion = trackVersion;
        }

        /// <summary>Samples recorded so far.</summary>
        public int SampleCount
        {
            get { return _count; }
        }

        /// <summary>True once <see cref="GhostPath.MaxSamples"/> samples were recorded (later observations are ignored).</summary>
        public bool IsFull
        {
            get { return _count >= GhostPath.MaxSamples; }
        }

        /// <summary>
        /// Adds an observation of the craft at (<paramref name="x"/>, <paramref name="y"/>); <paramref name="time"/> is the
        /// run's simulated time (pauses excluded). Observations that do not move forward are ignored. If the first
        /// observation is past distance 0, the path is assumed to start there at the same position.
        /// </summary>
        public void Record(double distance, float x, float y, double time)
        {
            if (double.IsNaN(distance) || double.IsInfinity(distance) || float.IsNaN(x) || float.IsInfinity(x) ||
                float.IsNaN(y) || float.IsInfinity(y) || double.IsNaN(time) || double.IsInfinity(time))
            {
                return;
            }

            if (!_hasPrevious)
            {
                _hasPrevious = true;
                _prevDistance = 0.0;
                _prevX = x;
                _prevY = y;
                _prevTime = distance > 0.0 ? 0.0 : Math.Max(0.0, time);
                Emit(x, y, _prevTime);
                if (distance <= 0.0)
                {
                    return;
                }
            }

            if (distance <= _prevDistance)
            {
                return;
            }

            double span = distance - _prevDistance;
            double next = _count * (double)GhostPath.SampleSpacing;
            while (!IsFull && next <= distance)
            {
                double f = (next - _prevDistance) / span;
                float sx = _prevX + (x - _prevX) * (float)f;
                float sy = _prevY + (y - _prevY) * (float)f;
                double t = _prevTime + (time - _prevTime) * f;
                Emit(sx, sy, t);
                next = _count * (double)GhostPath.SampleSpacing;
            }

            _prevDistance = distance;
            _prevX = x;
            _prevY = y;
            _prevTime = time;
        }

        /// <summary>The recording so far as a path (null with fewer than two samples, i.e. under 2 m).</summary>
        public GhostPath ToPath()
        {
            if (_count < 2)
            {
                return null;
            }

            int intervals = _count - 1;
            int checkpoints = _checkpointCount;
            int[] times = _checkpointMs;
            if (intervals % GhostPath.TimeCheckpointEvery != 0)
            {
                // Close the last partial segment with the last sample's time (not stored as a regular checkpoint).
                times = (int[])_checkpointMs.Clone();
                times[checkpoints] = _lastMs;
                checkpoints++;
            }

            return GhostPath.Build(_seed, _trackVersion, _positions, _count, times, checkpoints);
        }

        private void Emit(float x, float y, double time)
        {
            int ms = (int)Math.Min(int.MaxValue / 2, Math.Round(Math.Max(0.0, time) * 1000.0));
            if (_count > 0 && ms < _lastMs)
            {
                ms = _lastMs; // time never runs backwards along the path
            }

            _positions[_count * 2] = (byte)GhostPath.Quantise(x);
            _positions[_count * 2 + 1] = (byte)GhostPath.Quantise(y);
            if (_count % GhostPath.TimeCheckpointEvery == 0)
            {
                _checkpointMs[_checkpointCount++] = ms;
            }

            _lastMs = ms;
            _count++;
        }
    }
}

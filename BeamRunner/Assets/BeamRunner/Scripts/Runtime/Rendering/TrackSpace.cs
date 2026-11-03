using BeamRunner.Core.Field;
using BeamRunner.Core.Sim;
using UnityEngine;

namespace BeamRunner.Rendering
{
    /// <summary>
    /// The bent space the track is drawn in: every level is ridden flat along +Z, and at its wall the track turns 90°
    /// up the wall (a quarter pipe through the wall tile), so the next level runs straight up the wall ahead. The
    /// simulation stays a straight line; only the drawing bends.
    /// </summary>
    /// <remarks>
    /// <para><b>Frames.</b> Positions are expressed in the frame of the level the craft is in: flat from the end of the
    /// bend that led into it (<c>inEnd</c>, the level's start; none on a run's first level) to the start of the bend out
    /// of it (<c>outStart</c>, its wall tile). The bend out is a quarter circle of every height <c>y</c> around the axis
    /// <see cref="PivotY"/> over <see cref="BendLength"/> metres of track, after which the next level climbs straight up
    /// at height <c>PivotY + (s − outStart − BendLength)</c> and depth <c>PivotY − y</c> beyond the bend. The bend into the
    /// level mirrors it behind the craft (the previous level comes down from above-behind).</para>
    /// <para><b>Streaming.</b> As everywhere else the craft stays at world z = 0: track position <c>s</c> is drawn at
    /// <c>s − origin</c>, origin = the craft's distance — until the bend starts. While the craft rides the bend the origin
    /// stays at the bend's start and the whole bent space instead turns about the pivot by <see cref="BendAngle"/> (0 → 90°)
    /// so the craft keeps riding along +Z at its own height and the wall comes down to become the track ahead. The
    /// renderers of the track, pickups and surface draw in <see cref="Map"/> space under the transform that
    /// <see cref="ApplyTo"/> sets (the bend root); anything drawn in world space (FX, the ghost) goes through
    /// <see cref="ToWorld(float, float, double)"/>. When the bend completes the next level becomes the frame and the angle
    /// drops back to 0: both descriptions place everything at the same world position, so nothing jumps.</para>
    /// </remarks>
    public static class TrackSpace
    {
        /// <summary>Height of the bend's axis: a metre above the ceiling, so every rail (and the ceiling) bends around it.</summary>
        public const float PivotY = FieldSpec.CeilingY + 1f;

        /// <summary>Track metres a bend takes (the wall tile).</summary>
        public const float BendLength = FieldSpec.TileLength;

        private const float HalfPi = Mathf.PI * 0.5f;

        private static double _origin;
        private static double _inEnd = double.NegativeInfinity;
        private static double _outStart = double.PositiveInfinity;
        private static double _nextOutStart = double.PositiveInfinity;
        private static int _turns;
        private static float _angle;
        private static Quaternion _rotation = Quaternion.identity;
        private static Vector3 _pivot;

        /// <summary>Degrees the bent space is turned about the pivot (0 off the bend, 90 at its end).</summary>
        public static float BendAngle
        {
            get { return _angle; }
        }

        /// <summary>0 → 1 through the bend the craft is riding (0 off it).</summary>
        public static float Bend01
        {
            get { return _angle / 90f; }
        }

        /// <summary>Start (track metres) of the current frame's bend out (its wall); +∞ without one.</summary>
        public static double OutStart
        {
            get { return _outStart; }
        }

        /// <summary>End (track metres) of the bend into the current frame's level; −∞ without one.</summary>
        public static double InEnd
        {
            get { return _inEnd; }
        }

        /// <summary>Start of the next level's own bend (the top of the wall ahead); +∞ unknown.</summary>
        public static double NextOutStart
        {
            get { return _nextOutStart; }
        }

        /// <summary>
        /// The world's turn relative to where the run started (degrees about +X): 90 per wall climbed (mod 360) plus the
        /// bend in progress. The sky turns by it.
        /// </summary>
        public static float WorldTurn
        {
            get { return 90f * _turns + _angle; }
        }

        /// <summary>
        /// Frames the level <paramref name="sim"/>'s craft is in: flat from the bend that led into it (none on the run's
        /// first level) to its wall, where the track turns up into the next level.
        /// </summary>
        public static void SetFor(RunSimulation sim)
        {
            Course course = sim.Course;
            int level = sim.Level;
            double inEnd = level > course.StartLevel ? course.LevelStart(level) : double.NegativeInfinity;
            Set(sim.Distance, inEnd, FieldSpec.TileStart(course.WallTile(level)));
            _nextOutStart = FieldSpec.TileStart(course.WallTile(level + 1));
            _turns = (level - course.StartLevel) % 4;
        }

        /// <summary>
        /// Sets the frame: the craft at <paramref name="distance"/>, the bend into the level ending at
        /// <paramref name="inEnd"/> (−∞: none) and the bend out starting at <paramref name="outStart"/> (+∞: none).
        /// </summary>
        public static void Set(double distance, double inEnd, double outStart)
        {
            _inEnd = inEnd;
            _outStart = outStart > inEnd ? outStart : double.PositiveInfinity;
            _origin = distance < _outStart ? distance : _outStart;
            double into = distance - _outStart;
            _angle = into > 0.0 ? 90f * Mathf.Clamp01((float)(into / BendLength)) : 0f;
            _rotation = _angle > 0f ? Quaternion.Euler(_angle, 0f, 0f) : Quaternion.identity;
            _pivot = new Vector3(0f, PivotY, (float)(_outStart - _origin));
        }

        /// <summary>A flat, unbent frame streaming at <paramref name="distance"/> (hangar, no run).</summary>
        public static void SetFlat(double distance)
        {
            Set(distance, double.NegativeInfinity, double.PositiveInfinity);
            _nextOutStart = double.PositiveInfinity;
        }

        /// <summary>Poses the bend root (parent of everything drawn in <see cref="Map"/> space) for this frame.</summary>
        public static void ApplyTo(Transform bendRoot)
        {
            if (bendRoot == null)
            {
                return;
            }

            if (_angle <= 0f)
            {
                bendRoot.localPosition = Vector3.zero;
                bendRoot.localRotation = Quaternion.identity;
                return;
            }

            bendRoot.localRotation = _rotation;
            bendRoot.localPosition = _pivot - _rotation * _pivot;
        }

        /// <summary>
        /// Position of the track point (<paramref name="x"/>, <paramref name="y"/>) at track position
        /// <paramref name="s"/> in bend-root space.
        /// </summary>
        public static Vector3 Map(float x, float y, double s)
        {
            float r = PivotY - y;
            if (s < _inEnd)
            {
                double back = _inEnd - s;
                float pz = (float)(_inEnd - _origin);
                if (back <= BendLength)
                {
                    float phi = (float)(back / BendLength) * HalfPi;
                    return new Vector3(x, PivotY - r * Mathf.Cos(phi), pz - r * Mathf.Sin(phi));
                }

                return new Vector3(x, PivotY + (float)(back - BendLength), pz - r);
            }

            if (s <= _outStart)
            {
                return new Vector3(x, y, (float)(s - _origin));
            }

            double ahead = s - _outStart;
            float qz = (float)(_outStart - _origin);
            if (ahead <= BendLength)
            {
                float phi = (float)(ahead / BendLength) * HalfPi;
                return new Vector3(x, PivotY - r * Mathf.Cos(phi), qz + r * Mathf.Sin(phi));
            }

            return new Vector3(x, PivotY + (float)(ahead - BendLength), qz + r);
        }

        /// <summary>Orientation of the track at <paramref name="s"/> in bend-root space (identity on the flat).</summary>
        public static Quaternion Tilt(double s)
        {
            if (s < _inEnd)
            {
                float k = Mathf.Clamp01((float)((_inEnd - s) / BendLength));
                return Quaternion.Euler(90f * k, 0f, 0f);
            }

            if (s <= _outStart)
            {
                return Quaternion.identity;
            }

            float t = Mathf.Clamp01((float)((s - _outStart) / BendLength));
            return Quaternion.Euler(-90f * t, 0f, 0f);
        }

        /// <summary>True if <paramref name="s"/> lies on a bend (where straight pieces must be subdivided).</summary>
        public static bool IsBent(double s0, double s1)
        {
            return s0 < _inEnd && s1 > _inEnd - BendLength || s1 > _outStart && s0 < _outStart + BendLength;
        }

        /// <summary>A bend-root-space position in world space (under the world root).</summary>
        public static Vector3 ToWorld(Vector3 local)
        {
            return _angle > 0f ? _pivot + _rotation * (local - _pivot) : local;
        }

        /// <summary>A track point in world space.</summary>
        public static Vector3 ToWorld(float x, float y, double s)
        {
            return ToWorld(Map(x, y, s));
        }
    }
}

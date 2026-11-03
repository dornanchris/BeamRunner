using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BeamRunner.Rendering
{
    /// <summary>
    /// The megastructure the rails run along — a dark channel: its face far below the bottom layer
    /// (<see cref="FieldMath.FloorY"/>) between two void side walls (the tunnel, <see cref="WallHeight"/> high), the
    /// quarter pipe where it turns up at the end of the level (around the same axis as the rails' bend,
    /// <see cref="TrackSpace.PivotY"/>) and the wall ahead that the next level climbs, up to where that level turns again
    /// (overhead, far out of the usual view). One opaque mesh (Surface shader) in <see cref="TrackSpace"/>, rebuilt once
    /// per level; the wall wears the next level's palette and pattern from the start of the level, so the destination of
    /// every level is in view the whole way there.
    /// </summary>
    /// <remarks>
    /// The mesh is a path in the (y, z) plane extruded across x (the floor) and, at both edges, along the path's normal
    /// (the tunnel's side walls, which bend up the wall with it): uv = (x, or the height up a side wall; metres along the
    /// path), colour r = face (0 the face under the track, 1 the wall ahead and beyond), colour g = how much of the wall's
    /// lighter fog applies, colour b = side wall; normals toward the track. The bend into the level is built behind the craft too (the previous face rising behind it), so
    /// the frame change at the top of a bend shows the same surface.
    /// </remarks>
    public sealed class SurfaceView : MonoBehaviour
    {
        /// <summary>
        /// Half width (x) of the structure: a beam about three times as wide as the rails, so space shows on both sides
        /// of the track below and of the wall ahead (its edges glow; Surface shader).
        /// </summary>
        public const float HalfWidth = 14f;

        /// <summary>Radius of the surface's turn up into the wall (the rails' axis down to the surface).</summary>
        public const float BendRadius = TrackSpace.PivotY - FieldMath.FloorY;

        /// <summary>
        /// Height of the tunnel's side walls above the beam's face (m): their rim stays 3.5 m below the bottom rails, so
        /// the walls frame the drop under the track without closing in the view or drawing lines across it at eye level.
        /// Under the bend radius, so the walls never fold over themselves where they curve with the path.
        /// </summary>
        public const float WallHeight = 28f;

        /// <summary>Patterns the Surface shader knows (panels, ribs, diamonds).</summary>
        public const int PatternCount = 3;

        private const int ArcSegments = 24;
        private const float FarBehind = 900f;
        private const float FarAbove = 600f;

        /// <summary>Flat frames (no wall) shift the mesh by the track distance modulo this (every pattern period divides it).</summary>
        private const double FlatPeriod = 336.0;

        private readonly List<Vector3> _positions = new List<Vector3>(256);
        private readonly List<Vector3> _normals = new List<Vector3>(256);
        private readonly List<Vector2> _uvs = new List<Vector2>(256);
        private readonly List<Color32> _colors = new List<Color32>(256);
        private readonly List<int> _indices = new List<int>(1024);
        private readonly List<PathPoint> _path = new List<PathPoint>(128);

        private MeshRenderer _renderer;
        private Mesh _mesh;
        private double _builtIn = double.NaN;
        private double _builtOut = double.NaN;
        private double _builtNext = double.NaN;
        private bool _visible = true;

        /// <summary>Builds the (empty) surface under <paramref name="bendRoot"/>, the transform <see cref="TrackSpace.ApplyTo"/> poses.</summary>
        public static SurfaceView Create(Transform bendRoot)
        {
            var go = new GameObject("Surface");
            go.transform.SetParent(bendRoot, false);
            var view = go.AddComponent<SurfaceView>();
            view._mesh = new Mesh { name = "BeamRunner Surface" };
            view._mesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = view._mesh;
            view._renderer = go.AddComponent<MeshRenderer>();
            view._renderer.sharedMaterial = FieldMaterials.Surface();
            view._renderer.shadowCastingMode = ShadowCastingMode.Off;
            view._renderer.receiveShadows = false;
            view._renderer.lightProbeUsage = LightProbeUsage.Off;
            view._renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            RenderCache.Track(view._mesh);
            return view;
        }

        /// <summary>Surface pattern of a level: they cycle, so every wall leads onto a face that looks different.</summary>
        public static int PatternOf(int level)
        {
            return ((level - 1) % PatternCount + PatternCount) % PatternCount;
        }

        public void SetVisible(bool visible)
        {
            _visible = visible;
            _renderer.enabled = visible;
        }

        /// <summary>
        /// Places the surface for the current <see cref="TrackSpace"/> frame: <paramref name="inEnd"/> /
        /// <paramref name="outStart"/> as passed to <see cref="TrackSpace.Set"/>, <paramref name="nextOutStart"/> the start of
        /// the next level's own bend (the top of the wall ahead; +∞ unknown), <paramref name="distance"/> the craft's.
        /// </summary>
        public void Sync(double distance, double inEnd, double outStart, double nextOutStart)
        {
            bool flat = double.IsPositiveInfinity(outStart);
            if (flat)
            {
                inEnd = double.NegativeInfinity;
                nextOutStart = double.PositiveInfinity;
            }

            if (!SameFrame(inEnd, outStart, nextOutStart))
            {
                Build(inEnd, outStart, nextOutStart);
            }

            Transform t = transform;
            if (flat)
            {
                double phase = distance % FlatPeriod;
                if (phase < 0.0)
                {
                    phase += FlatPeriod;
                }

                t.localPosition = new Vector3(0f, 0f, -(float)phase);
            }
            else
            {
                // The mesh is built around the bend's start: put it where TrackSpace draws that point.
                t.localPosition = new Vector3(0f, 0f, TrackSpace.Map(0f, 0f, outStart).z);
            }

            _renderer.enabled = _visible;
        }

        private bool SameFrame(double inEnd, double outStart, double nextOutStart)
        {
            return Same(inEnd, _builtIn) && Same(outStart, _builtOut) && Same(nextOutStart, _builtNext);
        }

        private static bool Same(double a, double b)
        {
            return a == b || double.IsNaN(a) && double.IsNaN(b);
        }

        // ------------------------------------------------------------------ mesh

        private void Build(double inEnd, double outStart, double nextOutStart)
        {
            _builtIn = inEnd;
            _builtOut = outStart;
            _builtNext = nextOutStart;
            _positions.Clear();
            _normals.Clear();
            _uvs.Clear();
            _colors.Clear();
            _indices.Clear();

            float floor = FieldMath.FloorY;
            float pivot = TrackSpace.PivotY;
            float r = BendRadius;
            bool flat = double.IsPositiveInfinity(outStart);
            float v = 0f;

            if (flat)
            {
                Point(floor, -FarBehind, Vector3.up, -FarBehind, 0, 0f);
                Point(floor, FarBehind, Vector3.up, FarBehind, 0, 0f);
                Upload();
                return;
            }

            // Behind: the previous face rising behind the level's start, then the bend down onto this face.
            if (!double.IsNegativeInfinity(inEnd))
            {
                float back = (float)(outStart - inEnd); // this face's flat length
                float zp = -back;
                float arc = r * Mathf.PI * 0.5f;
                Point(pivot + FarAbove, zp - r, Vector3.forward, -back - arc - FarAbove, 0, 0f);
                for (int k = 0; k <= ArcSegments; k++)
                {
                    float phi = Mathf.PI * 0.5f * (1f - k / (float)ArcSegments);
                    float y = pivot - r * Mathf.Cos(phi);
                    float z = zp - r * Mathf.Sin(phi);
                    var n = new Vector3(0f, pivot - y, zp - z) / r;
                    Point(y, z, n, -back - arc * (1f - k / (float)ArcSegments), 0, 0f);
                }
            }
            else
            {
                Point(floor, -FarBehind, Vector3.up, -FarBehind, 0, 0f);
            }

            // This face up to the bend, the quarter pipe, and the wall ahead.
            Point(floor, 0f, Vector3.up, 0f, 0, 0f);
            float quarter = r * Mathf.PI * 0.5f;
            for (int k = 1; k <= ArcSegments; k++)
            {
                float t = k / (float)ArcSegments;
                float phi = Mathf.PI * 0.5f * t;
                float y = pivot - r * Mathf.Cos(phi);
                float z = r * Mathf.Sin(phi);
                var n = new Vector3(0f, pivot - y, -z) / r;
                Point(y, z, n, quarter * t, k == ArcSegments ? 1 : 0, t * t);
            }

            v = quarter;
            float height = double.IsPositiveInfinity(nextOutStart)
                ? FarAbove
                : Mathf.Max(0f, (float)(nextOutStart - outStart - TrackSpace.BendLength));
            Point(pivot + height, r, Vector3.back, v + height, 1, 1f);
            v += height;

            // The next level's own turn at the top of the wall, onto a face far overhead coming back toward the craft.
            if (!double.IsPositiveInfinity(nextOutStart))
            {
                float top = pivot + height;
                for (int k = 1; k <= ArcSegments; k++)
                {
                    float t = k / (float)ArcSegments;
                    float psi = Mathf.PI * 0.5f * t;
                    float y = top + r * Mathf.Sin(psi);
                    float z = r * Mathf.Cos(psi);
                    var n = new Vector3(0f, top - y, -z) / r;
                    Point(y, z, n, v + quarter * t, 1, 1f);
                }

                Point(top + r, -FarBehind, Vector3.down, v + quarter + FarBehind, 1, 1f);
            }

            Upload();
        }

        private struct PathPoint
        {
            public float Y;
            public float Z;
            public Vector3 Normal;
            public float V;
            public Color32 Color;
        }

        /// <summary>Adds a point of the path (the floor's centre line, its normal toward the track).</summary>
        private void Point(float y, float z, Vector3 normal, float v, int face, float wall)
        {
            _path.Add(new PathPoint
            {
                Y = y,
                Z = z,
                Normal = normal,
                V = v,
                Color = new Color32((byte)(face * 255), (byte)Mathf.RoundToInt(Mathf.Clamp01(wall) * 255f), 0, 255),
            });
        }

        /// <summary>
        /// The floor strip across the path, then the tunnel's two side walls: from each edge of the floor, along the path's
        /// normal (up from the face, toward the craft from a wall ahead), <see cref="WallHeight"/> high. Colour b marks the
        /// walls; their uv.x is the height up the wall.
        /// </summary>
        private void Upload()
        {
            Strip(-HalfWidth, HalfWidth, false);
            Strip(-HalfWidth, -HalfWidth, true);
            Strip(HalfWidth, HalfWidth, true);
            _mesh.Clear();
            _mesh.SetVertices(_positions);
            _mesh.SetNormals(_normals);
            _mesh.SetUVs(0, _uvs);
            _mesh.SetColors(_colors);
            _mesh.SetTriangles(_indices, 0, false);
            _mesh.bounds = new Bounds(new Vector3(0f, 0f, 0f), new Vector3(HalfWidth * 2f, 4000f, 4000f));
            _path.Clear();
        }

        /// <summary>One strip along the path: the floor (from x <paramref name="x0"/> to <paramref name="x1"/>) or a side wall at x <paramref name="x0"/>.</summary>
        private void Strip(float x0, float x1, bool wall)
        {
            int first = _positions.Count;
            for (int k = 0; k < _path.Count; k++)
            {
                PathPoint p = _path[k];
                Color32 c = p.Color;
                c.b = (byte)(wall ? 255 : 0);
                var a = new Vector3(x0, p.Y, p.Z);
                Vector3 b = wall ? a + p.Normal * WallHeight : new Vector3(x1, p.Y, p.Z);
                Vector3 n = wall ? new Vector3(-Mathf.Sign(x0), 0f, 0f) : p.Normal;
                _positions.Add(a);
                _positions.Add(b);
                _normals.Add(n);
                _normals.Add(n);
                _uvs.Add(new Vector2(wall ? 0f : x0, p.V));
                _uvs.Add(new Vector2(wall ? WallHeight : x1, p.V));
                _colors.Add(c);
                _colors.Add(c);
                if (k > 0)
                {
                    int i = first + k * 2;
                    _indices.Add(i - 2);
                    _indices.Add(i);
                    _indices.Add(i - 1);
                    _indices.Add(i - 1);
                    _indices.Add(i);
                    _indices.Add(i + 1);
                }
            }
        }

        private void OnDestroy()
        {
            if (_mesh != null)
            {
                RenderCache.Release(_mesh);
            }
        }
    }
}

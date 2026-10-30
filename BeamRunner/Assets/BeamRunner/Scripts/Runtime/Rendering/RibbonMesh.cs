using UnityEngine;
using UnityEngine.Rendering;

namespace BeamRunner.Rendering
{
    /// <summary>
    /// A dynamic mesh of one or more ribbon strips rebuilt every frame without allocations (the craft's
    /// trail). Topology is fixed at capacity; unused points are collapsed onto the last used
    /// point with zero alpha, so only vertex data is uploaded each frame. For the Additive shader
    /// (uv.x = 0..1 across, uv.y = caller-defined along; vertex alpha fades).
    /// </summary>
    public sealed class RibbonMesh
    {
        private static readonly Color32 Clear32 = new Color32(0, 0, 0, 0);

        private readonly Mesh _mesh;
        private readonly Vector3[] _positions;
        private readonly Color32[] _colors;
        private readonly Vector2[] _uvs;
        private readonly int _strips;
        private readonly int _points;
        private int _strip;
        private int _cursor;

        public RibbonMesh(string name, int strips, int pointsPerStrip, Bounds bounds)
        {
            _strips = Mathf.Max(1, strips);
            _points = Mathf.Max(2, pointsPerStrip);
            int vertexCount = _strips * _points * 2;
            _positions = new Vector3[vertexCount];
            _colors = new Color32[vertexCount];
            _uvs = new Vector2[vertexCount];

            var indices = new int[_strips * (_points - 1) * 6];
            int t = 0;
            for (int s = 0; s < _strips; s++)
            {
                for (int i = 0; i < _points - 1; i++)
                {
                    int l0 = (s * _points + i) * 2;
                    int r0 = l0 + 1;
                    int l1 = l0 + 2;
                    int r1 = l0 + 3;
                    indices[t++] = l0;
                    indices[t++] = l1;
                    indices[t++] = r0;
                    indices[t++] = r0;
                    indices[t++] = l1;
                    indices[t++] = r1;
                }
            }

            _mesh = new Mesh();
            _mesh.name = name;
            _mesh.indexFormat = vertexCount > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            _mesh.MarkDynamic();
            _mesh.vertices = _positions;
            _mesh.colors32 = _colors;
            _mesh.uv = _uvs;
            _mesh.triangles = indices;
            _mesh.bounds = bounds;
            RenderCache.Track(_mesh);
        }

        public Mesh Mesh
        {
            get { return _mesh; }
        }

        public int PointsPerStrip
        {
            get { return _points; }
        }

        /// <summary>Starts writing strip <paramref name="strip"/> from its first point.</summary>
        public void BeginStrip(int strip)
        {
            _strip = Mathf.Clamp(strip, 0, _strips - 1);
            _cursor = 0;
        }

        /// <summary>Adds a cross-section: centre ± halfWidth, with colour (alpha fades) and along-coordinate v.</summary>
        public void AddPoint(Vector3 centre, Vector3 halfWidth, Color32 color, float v)
        {
            if (_cursor >= _points)
            {
                return;
            }

            int idx = (_strip * _points + _cursor) * 2;
            _positions[idx] = centre - halfWidth;
            _positions[idx + 1] = centre + halfWidth;
            _colors[idx] = color;
            _colors[idx + 1] = color;
            _uvs[idx] = new Vector2(0f, v);
            _uvs[idx + 1] = new Vector2(1f, v);
            _cursor++;
        }

        /// <summary>True while the current strip still has room.</summary>
        public bool HasRoom
        {
            get { return _cursor < _points; }
        }

        /// <summary>Finishes the current strip, collapsing its unused points.</summary>
        public void EndStrip()
        {
            int start = _strip * _points;
            Vector3 anchor = _cursor > 0 ? (_positions[(start + _cursor - 1) * 2] + _positions[(start + _cursor - 1) * 2 + 1]) * 0.5f : Vector3.zero;
            for (int i = _cursor; i < _points; i++)
            {
                int idx = (start + i) * 2;
                _positions[idx] = anchor;
                _positions[idx + 1] = anchor;
                _colors[idx] = Clear32;
                _colors[idx + 1] = Clear32;
            }

            if (_cursor == 1)
            {
                // A single point cannot form a quad: hide it too.
                _colors[start * 2] = Clear32;
                _colors[start * 2 + 1] = Clear32;
            }
        }

        /// <summary>Hides a whole strip.</summary>
        public void ClearStrip(int strip)
        {
            BeginStrip(strip);
            EndStrip();
        }

        /// <summary>Uploads vertex data (bounds stay as set at construction).</summary>
        public void Upload()
        {
            const MeshUpdateFlags flags = MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices;
            int n = _positions.Length;
            _mesh.SetVertices(_positions, 0, n, flags);
            _mesh.SetColors(_colors, 0, n, flags);
            _mesh.SetUVs(0, _uvs, 0, n, flags);
        }
    }
}

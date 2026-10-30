using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BeamRunner.Rendering
{
    /// <summary>
    /// Accumulates flat-shaded, neon-edged geometry for the NeonSolid shader and writes it into a
    /// <see cref="Mesh"/>. Reusable: <see cref="Clear"/> keeps list capacity, so rebuilding meshes at runtime
    /// (gate arcs) does not allocate once warmed up.
    /// </summary>
    /// <remarks>
    /// <para><b>Winding is automatic.</b> Every face is given an "outward" hint; triangles are ordered so the
    /// front face (Unity: <c>cross(b − a, c − a)</c> points toward the viewer) faces along the hint. This
    /// also makes mirrored parts correct without special cases.</para>
    /// <para><b>Edge data.</b> UV1 (TEXCOORD1, float4) holds metric distances from the vertex to up to four
    /// real edges of its face; the shader draws the glow where <c>min(x, y, z, w)</c> is small, so edge width
    /// is uniform in metres on every face. Quads use (u·W, (1−u)·W, v·H, (1−v)·H); triangles use
    /// barycentric altitudes. Interior diagonals of triangulated polygons carry <see cref="NoEdge"/>, so only
    /// real outlines glow. UV0 is the per-face 0..1 parameterisation (face borders at 0 / 1).</para>
    /// <para>An optional placement transform (offset + basis) applies to all incoming points and hints, so a
    /// part can be built once in local coordinates and placed, rotated or mirrored.</para>
    /// </remarks>
    public sealed class MeshBuilder
    {
        /// <summary>Edge distance used for "not an edge".</summary>
        public const float NoEdge = 1000f;

        private readonly List<Vector3> _positions = new List<Vector3>(512);
        private readonly List<Vector3> _normals = new List<Vector3>(512);
        private readonly List<Vector2> _uv0 = new List<Vector2>(512);
        private readonly List<Vector4> _edges = new List<Vector4>(512);
        private readonly List<Color32> _colors = new List<Color32>(512);
        private readonly List<int> _indices = new List<int>(1024);

        // Scratch for polygon triangulation.
        private readonly List<int> _polyIndex = new List<int>(32);
        private readonly List<Vector2> _poly2D = new List<Vector2>(32);

        private Vector3 _offset = Vector3.zero;
        private Vector3 _axisX = Vector3.right;
        private Vector3 _axisY = Vector3.up;
        private Vector3 _axisZ = Vector3.forward;

        /// <summary>Vertex colour for subsequently added faces (edge tint in NeonSolid).</summary>
        public Color32 Color = new Color32(255, 255, 255, 255);

        public int VertexCount
        {
            get { return _positions.Count; }
        }

        public int IndexCount
        {
            get { return _indices.Count; }
        }

        public void Clear()
        {
            _positions.Clear();
            _normals.Clear();
            _uv0.Clear();
            _edges.Clear();
            _colors.Clear();
            _indices.Clear();
            ResetTransform();
            Color = new Color32(255, 255, 255, 255);
        }

        // ------------------------------------------------------------------ placement transform

        /// <summary>Places subsequent geometry: p' = offset + x·axisX + y·axisY + z·axisZ.</summary>
        public void SetTransform(Vector3 offset, Vector3 axisX, Vector3 axisY, Vector3 axisZ)
        {
            _offset = offset;
            _axisX = axisX;
            _axisY = axisY;
            _axisZ = axisZ;
        }

        /// <summary>Translation only.</summary>
        public void SetTransform(Vector3 offset)
        {
            SetTransform(offset, Vector3.right, Vector3.up, Vector3.forward);
        }

        /// <summary>Mirrors subsequent geometry across the YZ plane (x → −x), then offsets it.</summary>
        public void SetMirrorX(Vector3 offset)
        {
            SetTransform(offset, Vector3.left, Vector3.up, Vector3.forward);
        }

        public void ResetTransform()
        {
            SetTransform(Vector3.zero, Vector3.right, Vector3.up, Vector3.forward);
        }

        private Vector3 TransformPoint(Vector3 p)
        {
            return _offset + _axisX * p.x + _axisY * p.y + _axisZ * p.z;
        }

        private Vector3 TransformDirection(Vector3 d)
        {
            return _axisX * d.x + _axisY * d.y + _axisZ * d.z;
        }

        // ------------------------------------------------------------------ faces

        /// <summary>Flat triangle with all three edges glowing.</summary>
        public void AddTriangle(Vector3 a, Vector3 b, Vector3 c, Vector3 outward)
        {
            AddTriangle(a, b, c, outward, true, true, true);
        }

        /// <summary>Flat triangle; the flags choose which of edges ab, bc, ca glow.</summary>
        public void AddTriangle(Vector3 a, Vector3 b, Vector3 c, Vector3 outward, bool edgeAB, bool edgeBC, bool edgeCA)
        {
            a = TransformPoint(a);
            b = TransformPoint(b);
            c = TransformPoint(c);
            outward = TransformDirection(outward);
            AddTriangleWorld(a, b, c, outward, edgeAB, edgeBC, edgeCA);
        }

        private void AddTriangleWorld(Vector3 a, Vector3 b, Vector3 c, Vector3 outward, bool eAB, bool eBC, bool eCA)
        {
            Vector3 n = Vector3.Cross(b - a, c - a);
            float area2 = n.magnitude;
            if (area2 < 1e-9f)
            {
                return;
            }

            if (Vector3.Dot(n, outward) < 0f)
            {
                Vector3 t = b;
                b = c;
                c = t;
                bool te = eAB;
                eAB = eCA;
                eCA = te;
                n = -n;
            }

            n /= area2;

            // Altitudes: distance from each vertex to its opposite edge.
            float ha = area2 / Mathf.Max((c - b).magnitude, 1e-6f);
            float hb = area2 / Mathf.Max((a - c).magnitude, 1e-6f);
            float hc = area2 / Mathf.Max((b - a).magnitude, 1e-6f);

            // Component x: distance to edge bc (opposite a); y: to ca (opposite b); z: to ab (opposite c).
            float nx = eBC ? 0f : NoEdge;
            float ny = eCA ? 0f : NoEdge;
            float nz = eAB ? 0f : NoEdge;

            int i0 = _positions.Count;
            AddVertex(a, n, new Vector2(0f, 0f), new Vector4(eBC ? ha : NoEdge, ny, nz, NoEdge));
            AddVertex(b, n, new Vector2(1f, 0f), new Vector4(nx, eCA ? hb : NoEdge, nz, NoEdge));
            AddVertex(c, n, new Vector2(0.5f, 1f), new Vector4(nx, ny, eAB ? hc : NoEdge, NoEdge));
            _indices.Add(i0);
            _indices.Add(i0 + 1);
            _indices.Add(i0 + 2);
        }

        /// <summary>
        /// Flat quad p0 → p1 → p2 → p3 (in order around the face) with all four edges glowing. u runs p0 → p1,
        /// v runs p0 → p3.
        /// </summary>
        public void AddQuad(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Vector3 outward)
        {
            AddQuad(p0, p1, p2, p3, outward, true, true, true, true);
        }

        /// <summary>Flat quad; flags choose which edges (p0p1, p1p2, p2p3, p3p0) glow.</summary>
        public void AddQuad(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Vector3 outward,
            bool e01, bool e12, bool e23, bool e30)
        {
            p0 = TransformPoint(p0);
            p1 = TransformPoint(p1);
            p2 = TransformPoint(p2);
            p3 = TransformPoint(p3);
            outward = TransformDirection(outward);

            Vector3 n = Vector3.Cross(p1 - p0, p2 - p0) + Vector3.Cross(p2 - p0, p3 - p0);
            float len = n.magnitude;
            if (len < 1e-9f)
            {
                return;
            }

            n /= len;
            bool flip = Vector3.Dot(n, outward) < 0f;
            if (flip)
            {
                n = -n;
            }

            float w0 = (p1 - p0).magnitude;
            float w1 = (p2 - p3).magnitude;
            float h0 = (p3 - p0).magnitude;
            float h1 = (p2 - p1).magnitude;

            // x: distance to edge p3p0 (u = 0), y: to p1p2 (u = 1), z: to p0p1 (v = 0), w: to p2p3 (v = 1).
            Vector4 e0 = new Vector4(0f, w0, 0f, h0);
            Vector4 e1 = new Vector4(w0, 0f, 0f, h1);
            Vector4 e2 = new Vector4(w1, 0f, h1, 0f);
            Vector4 e3 = new Vector4(0f, w1, h0, 0f);
            if (!e30)
            {
                e0.x = e1.x = e2.x = e3.x = NoEdge;
            }

            if (!e12)
            {
                e0.y = e1.y = e2.y = e3.y = NoEdge;
            }

            if (!e01)
            {
                e0.z = e1.z = e2.z = e3.z = NoEdge;
            }

            if (!e23)
            {
                e0.w = e1.w = e2.w = e3.w = NoEdge;
            }

            int i0 = _positions.Count;
            AddVertex(p0, n, new Vector2(0f, 0f), e0);
            AddVertex(p1, n, new Vector2(1f, 0f), e1);
            AddVertex(p2, n, new Vector2(1f, 1f), e2);
            AddVertex(p3, n, new Vector2(0f, 1f), e3);
            AddQuadIndices(i0, flip);
        }

        /// <summary>
        /// Quad with caller-supplied per-vertex normals, UVs and edge distances (for curved, subdivided faces
        /// whose parameterisation spans several quads). Winding follows <paramref name="outward"/>.
        /// </summary>
        public void AddQuadRaw(
            Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3,
            Vector3 n0, Vector3 n1, Vector3 n2, Vector3 n3,
            Vector2 uv0, Vector2 uv1, Vector2 uv2, Vector2 uv3,
            Vector4 e0, Vector4 e1, Vector4 e2, Vector4 e3,
            Vector3 outward)
        {
            p0 = TransformPoint(p0);
            p1 = TransformPoint(p1);
            p2 = TransformPoint(p2);
            p3 = TransformPoint(p3);
            outward = TransformDirection(outward);

            Vector3 geo = Vector3.Cross(p1 - p0, p2 - p0) + Vector3.Cross(p2 - p0, p3 - p0);
            if (geo.sqrMagnitude < 1e-18f)
            {
                return;
            }

            bool flip = Vector3.Dot(geo, outward) < 0f;
            int i0 = _positions.Count;
            AddVertex(p0, TransformDirection(n0).normalized, uv0, e0);
            AddVertex(p1, TransformDirection(n1).normalized, uv1, e1);
            AddVertex(p2, TransformDirection(n2).normalized, uv2, e2);
            AddVertex(p3, TransformDirection(n3).normalized, uv3, e3);
            AddQuadIndices(i0, flip);
        }

        /// <summary>
        /// Flat planar polygon (convex or concave, simple) with its outline glowing and interior diagonals
        /// hidden. Points in order around the outline.
        /// </summary>
        public void AddPolygon(IList<Vector3> points, Vector3 outward)
        {
            int n = points.Count;
            if (n < 3)
            {
                return;
            }

            if (n == 3)
            {
                AddTriangle(points[0], points[1], points[2], outward);
                return;
            }

            if (n == 4 && IsConvex(points))
            {
                AddQuad(points[0], points[1], points[2], points[3], outward);
                return;
            }

            // Project onto the polygon plane (Newell normal) and ear-clip.
            Vector3 normal = Vector3.zero;
            for (int i = 0; i < n; i++)
            {
                Vector3 cur = points[i];
                Vector3 next = points[(i + 1) % n];
                normal.x += (cur.y - next.y) * (cur.z + next.z);
                normal.y += (cur.z - next.z) * (cur.x + next.x);
                normal.z += (cur.x - next.x) * (cur.y + next.y);
            }

            if (normal.sqrMagnitude < 1e-12f)
            {
                return;
            }

            normal.Normalize();
            Vector3 axisU = Vector3.Cross(normal, Mathf.Abs(normal.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            Vector3 axisV = Vector3.Cross(normal, axisU);

            _poly2D.Clear();
            _polyIndex.Clear();
            for (int i = 0; i < n; i++)
            {
                _poly2D.Add(new Vector2(Vector3.Dot(points[i], axisU), Vector3.Dot(points[i], axisV)));
                _polyIndex.Add(i);
            }

            // Ensure counter-clockwise order in 2D.
            float area = 0f;
            for (int i = 0; i < n; i++)
            {
                Vector2 a = _poly2D[i];
                Vector2 b = _poly2D[(i + 1) % n];
                area += a.x * b.y - b.x * a.y;
            }

            if (area < 0f)
            {
                _polyIndex.Reverse();
            }

            int guard = n * n + 8;
            while (_polyIndex.Count > 3 && guard-- > 0)
            {
                int m = _polyIndex.Count;
                bool clipped = false;
                for (int k = 0; k < m; k++)
                {
                    int ia = _polyIndex[(k + m - 1) % m];
                    int ib = _polyIndex[k];
                    int ic = _polyIndex[(k + 1) % m];
                    Vector2 a = _poly2D[ia];
                    Vector2 b = _poly2D[ib];
                    Vector2 c = _poly2D[ic];
                    if (Cross2(b - a, c - b) <= 1e-9f)
                    {
                        continue; // reflex or degenerate corner
                    }

                    bool contains = false;
                    for (int j = 0; j < m; j++)
                    {
                        int ip = _polyIndex[j];
                        if (ip == ia || ip == ib || ip == ic)
                        {
                            continue;
                        }

                        if (PointInTriangle(_poly2D[ip], a, b, c))
                        {
                            contains = true;
                            break;
                        }
                    }

                    if (contains)
                    {
                        continue;
                    }

                    AddTriangle(points[ia], points[ib], points[ic], outward,
                        IsOutlineEdge(ia, ib, n), IsOutlineEdge(ib, ic, n), IsOutlineEdge(ic, ia, n));
                    _polyIndex.RemoveAt(k);
                    clipped = true;
                    break;
                }

                if (!clipped)
                {
                    break; // numerically degenerate polygon: stop rather than loop forever
                }
            }

            if (_polyIndex.Count == 3)
            {
                int ia = _polyIndex[0];
                int ib = _polyIndex[1];
                int ic = _polyIndex[2];
                AddTriangle(points[ia], points[ib], points[ic], outward,
                    IsOutlineEdge(ia, ib, n), IsOutlineEdge(ib, ic, n), IsOutlineEdge(ic, ia, n));
            }
        }

        // ------------------------------------------------------------------ output

        /// <summary>Creates a new mesh (UInt16 indices when possible) with explicit, optionally inflated bounds.</summary>
        public Mesh ToMesh(string name, float boundsMargin = 0f)
        {
            var mesh = new Mesh();
            mesh.name = name;
            ApplyTo(mesh, boundsMargin);
            return mesh;
        }

        /// <summary>Writes the geometry into an existing mesh (clearing it first).</summary>
        public void ApplyTo(Mesh mesh, float boundsMargin = 0f)
        {
            mesh.Clear();
            mesh.indexFormat = _positions.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(_positions);
            mesh.SetNormals(_normals);
            mesh.SetUVs(0, _uv0);
            mesh.SetUVs(1, _edges);
            mesh.SetColors(_colors);
            mesh.SetTriangles(_indices, 0, true);
            if (boundsMargin > 0f)
            {
                Bounds b = mesh.bounds;
                b.Expand(new Vector3(boundsMargin * 2f, boundsMargin * 2f, 0f));
                mesh.bounds = b;
            }
        }

        // ------------------------------------------------------------------ internals

        private void AddVertex(Vector3 p, Vector3 n, Vector2 uv, Vector4 edge)
        {
            _positions.Add(p);
            _normals.Add(n);
            _uv0.Add(uv);
            _edges.Add(edge);
            _colors.Add(Color);
        }

        private void AddQuadIndices(int i0, bool flip)
        {
            if (!flip)
            {
                _indices.Add(i0);
                _indices.Add(i0 + 1);
                _indices.Add(i0 + 2);
                _indices.Add(i0);
                _indices.Add(i0 + 2);
                _indices.Add(i0 + 3);
            }
            else
            {
                _indices.Add(i0);
                _indices.Add(i0 + 2);
                _indices.Add(i0 + 1);
                _indices.Add(i0);
                _indices.Add(i0 + 3);
                _indices.Add(i0 + 2);
            }
        }

        private static bool IsOutlineEdge(int i, int j, int n)
        {
            int d = Mathf.Abs(i - j);
            return d == 1 || d == n - 1;
        }

        private static float Cross2(Vector2 a, Vector2 b)
        {
            return a.x * b.y - a.y * b.x;
        }

        private static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = Cross2(b - a, p - a);
            float d2 = Cross2(c - b, p - b);
            float d3 = Cross2(a - c, p - c);
            return d1 >= 0f && d2 >= 0f && d3 >= 0f;
        }

        private static bool IsConvex(IList<Vector3> q)
        {
            Vector3 n0 = Vector3.Cross(q[1] - q[0], q[2] - q[1]);
            Vector3 n1 = Vector3.Cross(q[2] - q[1], q[3] - q[2]);
            Vector3 n2 = Vector3.Cross(q[3] - q[2], q[0] - q[3]);
            Vector3 n3 = Vector3.Cross(q[0] - q[3], q[1] - q[0]);
            return Vector3.Dot(n0, n1) > 0f && Vector3.Dot(n1, n2) > 0f && Vector3.Dot(n2, n3) > 0f;
        }
    }
}

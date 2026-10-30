using System.Collections.Generic;
using BeamRunner.Core;
using BeamRunner.Core.Field;
using UnityEngine;
using UnityEngine.Rendering;

namespace BeamRunner.Rendering
{
    /// <summary>
    /// Procedural meshes, built once and cached (or rebuilt into a caller-owned mesh for per-instance
    /// geometry such as gate walls). All flat-shaded solids carry the NeonSolid edge data described on
    /// <see cref="MeshBuilder"/>. Indices are UInt16. Main thread only.
    /// </summary>
    /// <remarks>
    /// NeonSolid edge widths are metres in mesh space, so obstacles are built at their real size (the cube cache below
    /// holds one mesh per quantised size) rather than scaled unit meshes, whose glow would scale with them.
    /// </remarks>
    public static partial class MeshFactory
    {
        /// <summary>Box face flags for <see cref="AddBox"/>.</summary>
        public const int FacePosX = 1;

        public const int FaceNegX = 2;
        public const int FacePosY = 4;
        public const int FaceNegY = 8;
        public const int FacePosZ = 16;
        public const int FaceNegZ = 32;
        public const int AllFaces = 63;

        /// <summary>Quantisation step (m) of the cube cache's half sizes.</summary>
        public const float CubeSizeStep = 0.02f;

        private static readonly MeshBuilder Builder = new MeshBuilder();
        private static readonly Dictionary<string, Mesh> Cache = new Dictionary<string, Mesh>(32);
        private static readonly Dictionary<int, Mesh> CubeCache = new Dictionary<int, Mesh>(64);

        // ------------------------------------------------------------------ environment

        /// <summary>
        /// Horizontal plane at y = 0 spanning [xMin, xMax] × [zMin, zMax] (normal +Y, uv = (x, z) in metres), for the
        /// Grid shader (which works from world positions; a few subdivisions keep huge triangles off the GPU's clipper).
        /// </summary>
        public static Mesh GridPlane(float xMin, float xMax, float zMin, float zMax, int segmentsX, int segmentsZ)
        {
            segmentsX = Mathf.Clamp(segmentsX, 1, 64);
            segmentsZ = Mathf.Clamp(segmentsZ, 1, 64);
            string key = "grid:" + xMin + ":" + xMax + ":" + zMin + ":" + zMax + ":" + segmentsX + ":" + segmentsZ;
            Mesh cached;
            if (Cache.TryGetValue(key, out cached) && cached != null)
            {
                return cached;
            }

            int cols = segmentsX + 1;
            int rows = segmentsZ + 1;
            int count = cols * rows;
            var positions = new Vector3[count];
            var normals = new Vector3[count];
            var uvs = new Vector2[count];
            var colors = new Color32[count];
            var white = new Color32(255, 255, 255, 255);
            for (int j = 0; j < rows; j++)
            {
                float z = Mathf.Lerp(zMin, zMax, (float)j / segmentsZ);
                for (int i = 0; i < cols; i++)
                {
                    float x = Mathf.Lerp(xMin, xMax, (float)i / segmentsX);
                    int idx = j * cols + i;
                    positions[idx] = new Vector3(x, 0f, z);
                    normals[idx] = Vector3.up;
                    uvs[idx] = new Vector2(x, z);
                    colors[idx] = white;
                }
            }

            var indices = new int[segmentsX * segmentsZ * 6];
            int t = 0;
            for (int j = 0; j < segmentsZ; j++)
            {
                for (int i = 0; i < segmentsX; i++)
                {
                    int a = j * cols + i;
                    int b = a + 1;
                    int c = a + cols;
                    int d = c + 1;

                    // Front faces point up (cross(c − a, b − a) = +Y); the Grid shader draws both sides anyway.
                    indices[t++] = a;
                    indices[t++] = c;
                    indices[t++] = b;
                    indices[t++] = b;
                    indices[t++] = c;
                    indices[t++] = d;
                }
            }

            var mesh = new Mesh();
            mesh.name = "BeamRunner Grid Plane";
            mesh.indexFormat = count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.vertices = positions;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.colors32 = colors;
            mesh.triangles = indices;
            mesh.bounds = new Bounds(
                new Vector3((xMin + xMax) * 0.5f, 0f, (zMin + zMax) * 0.5f),
                new Vector3(xMax - xMin, 1f, zMax - zMin));
            return Store(key, mesh);
        }

        /// <summary>
        /// Sphere for the Sky shader, which re-centres it on the camera in its vertex stage: its bounds are inflated to a
        /// 2 km box so CPU frustum culling never drops it, wherever the camera looks.
        /// </summary>
        public static Mesh SkySphere()
        {
            Mesh cached;
            if (Cache.TryGetValue("sky", out cached) && cached != null)
            {
                return cached;
            }

            Mesh mesh = Object.Instantiate(Sphere(32, 16));
            mesh.name = "BeamRunner Sky Sphere";
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(2000f, 2000f, 2000f));
            return Store("sky", mesh);
        }

        // ------------------------------------------------------------------ boxes, cubes and gate walls

        /// <summary>
        /// Appends the selected faces (<see cref="FacePosX"/> …) of an axis-aligned box centred at
        /// <paramref name="center"/> with half extents <paramref name="half"/>. Every face is outlined in neon; on the
        /// vertical faces v runs up, on the horizontal ones along +Z.
        /// </summary>
        public static void AddBox(MeshBuilder b, Vector3 center, Vector3 half, int faces)
        {
            float x0 = center.x - half.x;
            float x1 = center.x + half.x;
            float y0 = center.y - half.y;
            float y1 = center.y + half.y;
            float z0 = center.z - half.z;
            float z1 = center.z + half.z;
            if ((faces & FaceNegZ) != 0)
            {
                b.AddQuad(new Vector3(x0, y0, z0), new Vector3(x1, y0, z0), new Vector3(x1, y1, z0), new Vector3(x0, y1, z0), Vector3.back);
            }

            if ((faces & FacePosZ) != 0)
            {
                b.AddQuad(new Vector3(x0, y0, z1), new Vector3(x1, y0, z1), new Vector3(x1, y1, z1), new Vector3(x0, y1, z1), Vector3.forward);
            }

            if ((faces & FaceNegX) != 0)
            {
                b.AddQuad(new Vector3(x0, y0, z0), new Vector3(x0, y0, z1), new Vector3(x0, y1, z1), new Vector3(x0, y1, z0), Vector3.left);
            }

            if ((faces & FacePosX) != 0)
            {
                b.AddQuad(new Vector3(x1, y0, z0), new Vector3(x1, y0, z1), new Vector3(x1, y1, z1), new Vector3(x1, y1, z0), Vector3.right);
            }

            if ((faces & FaceNegY) != 0)
            {
                b.AddQuad(new Vector3(x0, y0, z0), new Vector3(x1, y0, z0), new Vector3(x1, y0, z1), new Vector3(x0, y0, z1), Vector3.down);
            }

            if ((faces & FacePosY) != 0)
            {
                b.AddQuad(new Vector3(x0, y1, z0), new Vector3(x1, y1, z0), new Vector3(x1, y1, z1), new Vector3(x0, y1, z1), Vector3.up);
            }
        }

        /// <summary>Half size the cube cache uses for <paramref name="halfSize"/> (rounded to <see cref="CubeSizeStep"/>).</summary>
        public static float QuantizeCubeHalfSize(float halfSize)
        {
            int key = Mathf.Max(1, Mathf.RoundToInt(halfSize / CubeSizeStep));
            return key * CubeSizeStep;
        }

        /// <summary>
        /// Cached neon-edged cube with half size <see cref="QuantizeCubeHalfSize"/>(<paramref name="halfSize"/>), centred
        /// at the origin. Scale the renderer by halfSize / quantised (within 1 %) for the exact size: edge widths stay
        /// right because the size is baked into the mesh. No allocation once a size is cached.
        /// </summary>
        public static Mesh Cube(float halfSize)
        {
            int key = Mathf.Max(1, Mathf.RoundToInt(halfSize / CubeSizeStep));
            Mesh cached;
            if (CubeCache.TryGetValue(key, out cached) && cached != null)
            {
                return cached;
            }

            float h = key * CubeSizeStep;
            Builder.Clear();
            AddBox(Builder, Vector3.zero, new Vector3(h, h, h), AllFaces);
            Mesh mesh = Builder.ToMesh("BeamRunner Cube");
            RenderCache.Track(mesh);
            CubeCache[key] = mesh;
            return mesh;
        }

        /// <summary>
        /// Builds every cached cube size from <paramref name="minHalf"/> to <paramref name="maxHalf"/> up front (about 24
        /// vertices each), so cubes entering play never create a mesh mid-run.
        /// </summary>
        public static void PrewarmCubes(float minHalf, float maxHalf)
        {
            int k0 = Mathf.Max(1, Mathf.RoundToInt(minHalf / CubeSizeStep));
            int k1 = Mathf.Min(1000, Mathf.RoundToInt(maxHalf / CubeSizeStep));
            for (int k = k0; k <= k1; k++)
            {
                Cube(k * CubeSizeStep);
            }
        }

        /// <summary>Cached neon-edged box with half extents <paramref name="half"/>, centred at the origin.</summary>
        public static Mesh Box(Vector3 half)
        {
            string key = "box:" + half.x.ToString("F3") + ":" + half.y.ToString("F3") + ":" + half.z.ToString("F3");
            Mesh cached;
            if (Cache.TryGetValue(key, out cached) && cached != null)
            {
                return cached;
            }

            Builder.Clear();
            AddBox(Builder, Vector3.zero, half, AllFaces);
            return Store(key, Builder.ToMesh("BeamRunner Box"));
        }

        /// <summary>
        /// Extents (m, from the cell centre) of the gate block in pattern cell (<paramref name="c"/>, <paramref name="r"/>):
        /// a side facing another blocked cell (or a <paramref name="filler"/> cell, drawn by a separate block) is inset
        /// by half the visual <paramref name="gap"/>; a side facing an opening or the pattern's edge reaches the full cell
        /// boundary, so drawn openings are exactly the colliding ones (collision uses whole cells).
        /// </summary>
        public static void GateBlockExtents(bool[] blocked, int cols, int rows, int c, int r, float gap, bool[] filler,
            out float left, out float right, out float bottom, out float top)
        {
            float full = FieldSpec.CellSize * 0.5f;
            float inset = (FieldSpec.CellSize - gap) * 0.5f;
            left = IsBlockedOrFiller(blocked, cols, rows, c - 1, r, filler) ? inset : full;
            right = IsBlockedOrFiller(blocked, cols, rows, c + 1, r, filler) ? inset : full;
            bottom = IsBlockedOrFiller(blocked, cols, rows, c, r - 1, filler) ? inset : full;
            top = IsBlockedOrFiller(blocked, cols, rows, c, r + 1, filler) ? inset : full;
        }

        /// <summary>
        /// Appends a gate wall: one neon-outlined block per blocked cell of a <paramref name="cols"/> ×
        /// <paramref name="rows"/> pattern (row-major <paramref name="blocked"/>), pattern cell (originCol, originRow)
        /// centred on field cell (0, 0), <paramref name="depth"/> deep and centred on z = 0. Neighbouring blocks are
        /// separated by a visual <paramref name="gap"/>; sides facing an opening or the pattern's edge sit on the true cell
        /// boundary (<see cref="GateBlockExtents"/>). Front faces (−Z, toward the craft) always; side faces only where the
        /// neighbour is not blocked (also toward <paramref name="filler"/> cells, whose separate blocks may be animating;
        /// null = none); no back faces (never seen: the camera is always behind).
        /// </summary>
        public static void BuildGateWall(MeshBuilder b, bool[] blocked, int cols, int rows, int originCol, int originRow,
            float depth, float gap, bool[] filler = null)
        {
            float hz = depth * 0.5f;
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    if (!blocked[r * cols + c])
                    {
                        continue;
                    }

                    int faces = FaceNegZ;
                    if (!IsBlocked(blocked, cols, rows, c - 1, r))
                    {
                        faces |= FaceNegX;
                    }

                    if (!IsBlocked(blocked, cols, rows, c + 1, r))
                    {
                        faces |= FacePosX;
                    }

                    if (!IsBlocked(blocked, cols, rows, c, r - 1))
                    {
                        faces |= FaceNegY;
                    }

                    if (!IsBlocked(blocked, cols, rows, c, r + 1))
                    {
                        faces |= FacePosY;
                    }

                    float left;
                    float right;
                    float bottom;
                    float top;
                    GateBlockExtents(blocked, cols, rows, c, r, gap, filler, out left, out right, out bottom, out top);
                    float x = FieldSpec.CellCenter(c - originCol);
                    float y = FieldSpec.CellCenter(r - originRow);
                    var center = new Vector3(x + (right - left) * 0.5f, y + (top - bottom) * 0.5f, 0f);
                    var half = new Vector3((left + right) * 0.5f, (bottom + top) * 0.5f, hz);
                    AddBox(b, center, half, faces);
                }
            }
        }


        /// <summary>
        /// Appends the highlight frame of a gate's openings for the Additive shader (Beam texture: v runs across each
        /// strip): a soft strip of <paramref name="width"/> m along every border between an open cell and a blocked one,
        /// in the plane z = <paramref name="z"/>. Adjacent open cells form one window with no strip between them.
        /// </summary>
        public static void BuildOpeningFrame(MeshBuilder b, bool[] blocked, int cols, int rows, int originCol, int originRow,
            float z, float width)
        {
            float h = FieldSpec.CellSize * 0.5f;
            float e = width * 0.5f;
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    if (blocked[r * cols + c])
                    {
                        continue;
                    }

                    float x = FieldSpec.CellCenter(c - originCol);
                    float y = FieldSpec.CellCenter(r - originRow);
                    if (IsBlocked(blocked, cols, rows, c - 1, r))
                    {
                        AddVerticalStrip(b, x - h, y - h - e, y + h + e, z, e);
                    }

                    if (IsBlocked(blocked, cols, rows, c + 1, r))
                    {
                        AddVerticalStrip(b, x + h, y - h - e, y + h + e, z, e);
                    }

                    if (IsBlocked(blocked, cols, rows, c, r - 1))
                    {
                        AddHorizontalStrip(b, y - h, x - h - e, x + h + e, z, e);
                    }

                    if (IsBlocked(blocked, cols, rows, c, r + 1))
                    {
                        AddHorizontalStrip(b, y + h, x - h - e, x + h + e, z, e);
                    }
                }
            }
        }

        private static bool IsBlocked(bool[] blocked, int cols, int rows, int c, int r)
        {
            return c >= 0 && r >= 0 && c < cols && r < rows && blocked[r * cols + c];
        }

        private static bool IsBlockedOrFiller(bool[] blocked, int cols, int rows, int c, int r, bool[] filler)
        {
            return c >= 0 && r >= 0 && c < cols && r < rows &&
                   (blocked[r * cols + c] || (filler != null && filler[r * cols + c]));
        }

        private static void AddVerticalStrip(MeshBuilder b, float x, float y0, float y1, float z, float halfWidth)
        {
            // u along the strip (p0 → p1), v across it (p0 → p3).
            b.AddQuad(new Vector3(x - halfWidth, y0, z), new Vector3(x - halfWidth, y1, z),
                new Vector3(x + halfWidth, y1, z), new Vector3(x + halfWidth, y0, z), Vector3.back);
        }

        private static void AddHorizontalStrip(MeshBuilder b, float y, float x0, float x1, float z, float halfWidth)
        {
            b.AddQuad(new Vector3(x0, y - halfWidth, z), new Vector3(x1, y - halfWidth, z),
                new Vector3(x1, y + halfWidth, z), new Vector3(x0, y + halfWidth, z), Vector3.back);
        }

        // ------------------------------------------------------------------ pickups & primitives

        /// <summary>Faceted diamond (gem), centred at the origin, tip along +Y.</summary>
        public static Mesh Octahedron(float height = 0.42f, float width = 0.26f)
        {
            string key = "octa:" + height + ":" + width;
            Mesh cached;
            if (Cache.TryGetValue(key, out cached) && cached != null)
            {
                return cached;
            }

            Builder.Clear();
            Vector3 top = new Vector3(0f, height * 0.5f, 0f);
            Vector3 bottom = new Vector3(0f, -height * 0.5f, 0f);
            float r = width * 0.5f;
            for (int k = 0; k < 4; k++)
            {
                float a0 = k * MathUtil.HalfPi + MathUtil.Pi * 0.25f;
                float a1 = (k + 1) * MathUtil.HalfPi + MathUtil.Pi * 0.25f;
                Vector3 p0 = new Vector3(Mathf.Cos(a0) * r, 0f, Mathf.Sin(a0) * r);
                Vector3 p1 = new Vector3(Mathf.Cos(a1) * r, 0f, Mathf.Sin(a1) * r);
                Builder.AddTriangle(top, p0, p1, (top + p0 + p1) / 3f);
                Builder.AddTriangle(bottom, p1, p0, (bottom + p0 + p1) / 3f);
            }

            return Store(key, Builder.ToMesh("BeamRunner Octahedron"));
        }

        /// <summary>Faceted torus in the XY plane (axis along Z, so it faces the camera). Every facet edge glows.</summary>
        public static Mesh Torus(float majorRadius = 0.3f, float minorRadius = 0.075f, int majorSegments = 14, int minorSegments = 5)
        {
            majorSegments = Mathf.Max(3, majorSegments);
            minorSegments = Mathf.Max(3, minorSegments);
            string key = "torus:" + majorRadius + ":" + minorRadius + ":" + majorSegments + ":" + minorSegments;
            Mesh cached;
            if (Cache.TryGetValue(key, out cached) && cached != null)
            {
                return cached;
            }

            Builder.Clear();
            for (int i = 0; i < majorSegments; i++)
            {
                float p0 = MathUtil.TwoPi * i / majorSegments;
                float p1 = MathUtil.TwoPi * (i + 1) / majorSegments;
                Vector3 c0 = new Vector3(Mathf.Cos(p0), Mathf.Sin(p0), 0f);
                Vector3 c1 = new Vector3(Mathf.Cos(p1), Mathf.Sin(p1), 0f);
                Vector3 centreMid = (c0 + c1).normalized * majorRadius;
                for (int j = 0; j < minorSegments; j++)
                {
                    float q0 = MathUtil.TwoPi * j / minorSegments;
                    float q1 = MathUtil.TwoPi * (j + 1) / minorSegments;
                    Vector3 v00 = TorusPoint(c0, q0, majorRadius, minorRadius);
                    Vector3 v10 = TorusPoint(c1, q0, majorRadius, minorRadius);
                    Vector3 v11 = TorusPoint(c1, q1, majorRadius, minorRadius);
                    Vector3 v01 = TorusPoint(c0, q1, majorRadius, minorRadius);
                    Vector3 mid = (v00 + v10 + v11 + v01) * 0.25f;
                    Builder.AddQuad(v00, v10, v11, v01, mid - centreMid);
                }
            }

            return Store(key, Builder.ToMesh("BeamRunner Torus"));
        }

        private static Vector3 TorusPoint(Vector3 dir, float minorAngle, float majorRadius, float minorRadius)
        {
            return dir * (majorRadius + minorRadius * Mathf.Cos(minorAngle)) + Vector3.forward * (minorRadius * Mathf.Sin(minorAngle));
        }

        /// <summary>
        /// U-shaped magnet (opening up, +Y) in the XY plane. Coloured by vertex colour (NeonSolid multiplies the
        /// edge colour by it): a hot red body with silver-white pole tips, so use a white edge colour.
        /// </summary>
        public static Mesh Magnet()
        {
            Mesh cached;
            if (Cache.TryGetValue("magnet", out cached) && cached != null)
            {
                return cached;
            }

            Builder.Clear();
            const float bendRadius = 0.15f;
            const float half = 0.055f;
            const float legLength = 0.17f;
            const int arcSegments = 6;

            // Path: left leg top → down → semicircle through the bottom → up the right leg.
            var path = new List<Vector3>(arcSegments + 5);
            var sideDir = new List<Vector3>(arcSegments + 5);
            path.Add(new Vector3(-bendRadius, legLength, 0f));
            sideDir.Add(Vector3.left);
            path.Add(new Vector3(-bendRadius, 0.06f, 0f));
            sideDir.Add(Vector3.left);
            for (int k = 0; k <= arcSegments; k++)
            {
                float a = MathUtil.Pi + MathUtil.Pi * k / arcSegments; // π → 2π: left → bottom → right
                Vector3 dir = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                path.Add(dir * bendRadius);
                sideDir.Add(dir);
            }

            path.Add(new Vector3(bendRadius, 0.06f, 0f));
            sideDir.Add(Vector3.right);
            path.Add(new Vector3(bendRadius, legLength, 0f));
            sideDir.Add(Vector3.right);

            // Vertex colours reach the shader raw (no sRGB → linear conversion), so convert the authored sRGB
            // values for linear projects; otherwise the "hot red" body renders as pale pink.
            Color32 pole = FieldGlobals.ToShaderSpace(new Color32(225, 238, 255, 255));
            Color32 body = FieldGlobals.ToShaderSpace(new Color32(255, 46, 84, 255));
            int last = path.Count - 1;
            for (int k = 0; k < last; k++)
            {
                bool isPole = k == 0 || k == last - 1;
                Builder.Color = isPole ? pole : body;
                AddTubeSegment(Builder, path[k], sideDir[k], path[k + 1], sideDir[k + 1], half);
            }

            // End caps (the pole faces, facing up).
            Builder.Color = pole;
            AddTubeCap(Builder, path[0], sideDir[0], half, Vector3.up);
            AddTubeCap(Builder, path[last], sideDir[last], half, Vector3.up);
            return Store("magnet", Builder.ToMesh("BeamRunner Magnet"));
        }

        private static void AddTubeSegment(MeshBuilder b, Vector3 p0, Vector3 side0, Vector3 p1, Vector3 side1, float half)
        {
            Vector3 z = Vector3.forward * half;
            Vector3 s0 = side0 * half;
            Vector3 s1 = side1 * half;
            Vector3 outer0 = p0 + s0, inner0 = p0 - s0, outer1 = p1 + s1, inner1 = p1 - s1;
            b.AddQuad(outer0 - z, outer1 - z, outer1 + z, outer0 + z, (side0 + side1));
            b.AddQuad(inner0 - z, inner1 - z, inner1 + z, inner0 + z, -(side0 + side1));
            b.AddQuad(inner0 + z, inner1 + z, outer1 + z, outer0 + z, Vector3.forward);
            b.AddQuad(inner0 - z, inner1 - z, outer1 - z, outer0 - z, Vector3.back);
        }

        private static void AddTubeCap(MeshBuilder b, Vector3 p, Vector3 side, float half, Vector3 outward)
        {
            Vector3 z = Vector3.forward * half;
            Vector3 s = side * half;
            b.AddQuad(p - s - z, p + s - z, p + s + z, p - s + z, outward);
        }

        /// <summary>Lightning bolt (Overdrive cell) in the XY plane, extruded along Z.</summary>
        public static Mesh Bolt()
        {
            Mesh cached;
            if (Cache.TryGetValue("bolt", out cached) && cached != null)
            {
                return cached;
            }

            Builder.Clear();
            var outline = new[]
            {
                new Vector2(-0.03f, 0.3f),
                new Vector2(0.17f, 0.3f),
                new Vector2(0.06f, 0.06f),
                new Vector2(0.17f, 0.06f),
                new Vector2(-0.1f, -0.32f),
                new Vector2(-0.01f, -0.05f),
                new Vector2(-0.13f, -0.05f),
            };
            Extrude(Builder, outline, -0.045f, 0.045f);
            return Store("bolt", Builder.ToMesh("BeamRunner Bolt"));
        }

        /// <summary>Upward chevron ("^") in the XY plane, extruded along Z.</summary>
        public static Mesh Chevron()
        {
            Mesh cached;
            if (Cache.TryGetValue("chevron", out cached) && cached != null)
            {
                return cached;
            }

            Builder.Clear();
            var outline = new[]
            {
                new Vector2(0f, 0.2f),
                new Vector2(0.3f, -0.08f),
                new Vector2(0.3f, -0.21f),
                new Vector2(0f, 0.07f),
                new Vector2(-0.3f, -0.21f),
                new Vector2(-0.3f, -0.08f),
            };
            Extrude(Builder, outline, -0.04f, 0.04f);
            return Store("chevron", Builder.ToMesh("BeamRunner Chevron"));
        }

        /// <summary>Plus sign (repair) in the XY plane, extruded along Z.</summary>
        public static Mesh Plus()
        {
            Mesh cached;
            if (Cache.TryGetValue("plus", out cached) && cached != null)
            {
                return cached;
            }

            Builder.Clear();
            AddBox(Builder, Vector3.zero, new Vector3(0.24f, 0.075f, 0.05f), AllFaces);
            AddBox(Builder, new Vector3(0f, 0.155f, 0f), new Vector3(0.075f, 0.08f, 0.05f), AllFaces);
            AddBox(Builder, new Vector3(0f, -0.155f, 0f), new Vector3(0.075f, 0.08f, 0.05f), AllFaces);
            return Store("plus", Builder.ToMesh("BeamRunner Plus"));
        }

        /// <summary>Fuel cell: a small upright capsule-like canister with a cap.</summary>
        public static Mesh FuelCell()
        {
            Mesh cached;
            if (Cache.TryGetValue("fuelcell", out cached) && cached != null)
            {
                return cached;
            }

            Builder.Clear();
            AddBox(Builder, Vector3.zero, new Vector3(0.14f, 0.2f, 0.14f), AllFaces);
            AddBox(Builder, new Vector3(0f, 0.25f, 0f), new Vector3(0.07f, 0.05f, 0.07f), AllFaces);
            return Store("fuelcell", Builder.ToMesh("BeamRunner Fuel Cell"));
        }

        /// <summary>Appends a prism: the XY outline (simple polygon) from z0 to z1 with capped ends.</summary>
        public static void Extrude(MeshBuilder b, IList<Vector2> outline, float z0, float z1)
        {
            int n = outline.Count;
            if (n < 3)
            {
                return;
            }

            float area = 0f;
            for (int i = 0; i < n; i++)
            {
                Vector2 p = outline[i];
                Vector2 q = outline[(i + 1) % n];
                area += p.x * q.y - q.x * p.y;
            }

            float orient = area >= 0f ? 1f : -1f; // +1 for counter-clockwise
            var front = new Vector3[n];
            var back = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                front[i] = new Vector3(outline[i].x, outline[i].y, z0);
                back[i] = new Vector3(outline[i].x, outline[i].y, z1);
            }

            b.AddPolygon(front, Vector3.back);
            b.AddPolygon(back, Vector3.forward);
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                Vector2 e = outline[j] - outline[i];
                Vector3 outward = new Vector3(e.y, -e.x, 0f) * orient;
                b.AddQuad(front[i], front[j], back[j], back[i], outward);
            }
        }

        /// <summary>Unit quad in the XY plane facing −Z (toward the chase camera), uv 0..1.</summary>
        public static Mesh Quad()
        {
            Mesh cached;
            if (Cache.TryGetValue("quad", out cached) && cached != null)
            {
                return cached;
            }

            var mesh = new Mesh();
            mesh.name = "BeamRunner Quad";
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
                new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f),
            };
            mesh.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
            var w = new Color32(255, 255, 255, 255);
            mesh.colors32 = new[] { w, w, w, w };
            mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            mesh.RecalculateBounds();
            return Store("quad", mesh);
        }

        /// <summary>
        /// Two crossed quads trailing from the origin toward −Z (length 1, width 1): an engine flare seen well
        /// from above and from behind. uv.x = 1 at the engine (head of the Streak texture), 0 at the tail.
        /// </summary>
        public static Mesh FlareCross()
        {
            Mesh cached;
            if (Cache.TryGetValue("flare", out cached) && cached != null)
            {
                return cached;
            }

            var mesh = new Mesh();
            mesh.name = "BeamRunner Flare";
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, 0f, 0f), new Vector3(0.5f, 0f, 0f), new Vector3(-0.5f, 0f, -1f), new Vector3(0.5f, 0f, -1f),
                new Vector3(0f, -0.5f, 0f), new Vector3(0f, 0.5f, 0f), new Vector3(0f, -0.5f, -1f), new Vector3(0f, 0.5f, -1f),
            };
            mesh.uv = new[]
            {
                new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 0f), new Vector2(0f, 1f),
                new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 0f), new Vector2(0f, 1f),
            };
            mesh.normals = new[]
            {
                Vector3.up, Vector3.up, Vector3.up, Vector3.up,
                Vector3.right, Vector3.right, Vector3.right, Vector3.right,
            };
            var w = new Color32(255, 255, 255, 255);
            mesh.colors32 = new[] { w, w, w, w, w, w, w, w };
            mesh.triangles = new[] { 0, 1, 2, 2, 1, 3, 4, 5, 6, 6, 5, 7 };
            mesh.RecalculateBounds();
            return Store("flare", mesh);
        }

        /// <summary>Smooth UV sphere of radius 0.5 (shield bubble), outward normals.</summary>
        public static Mesh Sphere(int longitude = 24, int latitude = 14)
        {
            longitude = Mathf.Max(6, longitude);
            latitude = Mathf.Max(4, latitude);
            string key = "sphere:" + longitude + ":" + latitude;
            Mesh cached;
            if (Cache.TryGetValue(key, out cached) && cached != null)
            {
                return cached;
            }

            int cols = longitude + 1;
            int count = cols * (latitude + 1);
            var positions = new Vector3[count];
            var normals = new Vector3[count];
            var uvs = new Vector2[count];
            var colors = new Color32[count];
            var w = new Color32(255, 255, 255, 255);
            for (int j = 0; j <= latitude; j++)
            {
                float phi = MathUtil.Pi * j / latitude; // 0 = top
                for (int i = 0; i <= longitude; i++)
                {
                    float theta = MathUtil.TwoPi * i / longitude;
                    var n = new Vector3(Mathf.Sin(phi) * Mathf.Cos(theta), Mathf.Cos(phi), Mathf.Sin(phi) * Mathf.Sin(theta));
                    int idx = j * cols + i;
                    positions[idx] = n * 0.5f;
                    normals[idx] = n;
                    uvs[idx] = new Vector2((float)i / longitude, 1f - (float)j / latitude);
                    colors[idx] = w;
                }
            }

            var indices = new List<int>(longitude * latitude * 6);
            for (int j = 0; j < latitude; j++)
            {
                for (int i = 0; i < longitude; i++)
                {
                    int a = j * cols + i;
                    int b = a + 1;
                    int c = a + cols;
                    int d = c + 1;
                    AddOutwardTriangle(indices, positions, a, b, c);
                    AddOutwardTriangle(indices, positions, b, d, c);
                }
            }

            var mesh = new Mesh();
            mesh.name = "BeamRunner Sphere";
            mesh.vertices = positions;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.colors32 = colors;
            mesh.SetTriangles(indices, 0, true);
            return Store(key, mesh);
        }

        private static void AddOutwardTriangle(List<int> indices, Vector3[] p, int a, int b, int c)
        {
            Vector3 n = Vector3.Cross(p[b] - p[a], p[c] - p[a]);
            if (n.sqrMagnitude < 1e-14f)
            {
                return; // pole slivers
            }

            Vector3 centroid = (p[a] + p[b] + p[c]) / 3f;
            if (Vector3.Dot(n, centroid) >= 0f)
            {
                indices.Add(a);
                indices.Add(b);
                indices.Add(c);
            }
            else
            {
                indices.Add(a);
                indices.Add(c);
                indices.Add(b);
            }
        }

        // ------------------------------------------------------------------ cache

        private static Mesh Store(string key, Mesh mesh)
        {
            RenderCache.Track(mesh);
            Cache[key] = mesh;
            return mesh;
        }
    }
}

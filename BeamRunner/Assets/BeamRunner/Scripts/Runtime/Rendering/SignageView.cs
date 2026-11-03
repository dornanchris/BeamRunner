using System.Collections.Generic;
using BeamRunner.Core.Field;
using BeamRunner.Core.Sim;
using UnityEngine;
using UnityEngine.Rendering;

namespace BeamRunner.Rendering
{
    /// <summary>
    /// Scenery that carries the brand into any footage of the game: once per level a billboard reading
    /// <see cref="Brand"/> stands just off the track (Core's <see cref="Signs"/>: early in the level, alternating sides),
    /// a dark board with neon edges and glowing dot lettering on two posts that run down to the beam far below. It never
    /// collides: it stands outside the lanes, turned <see cref="SignYaw"/>° toward the track like a billboard facing
    /// oncoming traffic. Placed in <see cref="TrackSpace"/> (under the bend root) at its track position, so the next
    /// level's sign already stands out from the wall ahead and comes down with it through the bend. Ported from
    /// LightLancer's SignageView (without its flipped second plate and tunnel portals). One pooled sign, three draw calls
    /// while in view, no allocation per frame.
    /// </summary>
    public sealed class SignageView : MonoBehaviour
    {
        /// <summary>The lettering on the sign. The brand name is never translated.</summary>
        public const string Brand = "BEAMRUNNER"; // l10n-ok: brand

        /// <summary>Degrees the board is turned toward the track.</summary>
        public const float SignYaw = 22f;

        private const double Behind = 30.0;
        private const double Ahead = FieldMath.VisibleLength + 20.0;

        // The board, in its own space: origin at track height 0 below the board's centre, front face toward −Z.
        private const float SignClearance = 1.2f;
        private const float BoardHalfWidth = 4.4f;
        private const float PlateHalfHeight = 1.2f;
        private const float PlateHalfDepth = 0.12f;
        private const float PlateY = 3f;
        private const float PostInset = 0.9f;

        /// <summary>Distance of the board's centre from the track's centre line: its inner end clear of the lanes.</summary>
        private static readonly float CenterX =
            FieldSpec.HalfExtent + SignClearance + BoardHalfWidth * Mathf.Cos(SignYaw * Mathf.Deg2Rad);

        private Transform _sign;
        private bool _shown;

        public static SignageView Create(Transform bendRoot)
        {
            var go = new GameObject("Signage");
            go.transform.SetParent(bendRoot, false);
            var view = go.AddComponent<SignageView>();
            view.Build();
            return view;
        }

        /// <summary>
        /// Places the sign of <paramref name="sim"/>'s level or the next in the current <see cref="TrackSpace"/> frame (set
        /// first); <c>null</c> hides it (hangar).
        /// </summary>
        public void Sync(RunSimulation sim)
        {
            bool show = false;
            if (sim != null)
            {
                double distance = sim.Distance;
                for (int level = sim.Level; level <= sim.Level + 1 && !show; level++)
                {
                    double s = Signs.At(sim.Course, level);
                    if (double.IsNaN(s) || s < distance - Behind || s > distance + Ahead)
                    {
                        continue;
                    }

                    int side = Signs.SideOf(level);
                    _sign.localPosition = TrackSpace.Map(side * CenterX, 0f, s);
                    _sign.localRotation = TrackSpace.Tilt(s) * Quaternion.Euler(0f, side * SignYaw, 0f);
                    show = true;
                }
            }

            if (show != _shown)
            {
                _shown = show;
                _sign.gameObject.SetActive(show);
            }
        }

        // ------------------------------------------------------------------ building (once)

        private void Build()
        {
            float aspect;
            Texture2D text = BrandTexture(out aspect);

            _sign = new GameObject("Sign").transform;
            _sign.SetParent(transform, false);
            AddPart(_sign, "Board", BuildBoardMesh(), FieldMaterials.Obstacle(), ShaderIds.PaletteSecondary, -1f, false);
            AddPart(_sign, "Posts", BuildPostsMesh(), FieldMaterials.Obstacle(), ShaderIds.PaletteSecondary, 0.8f, false);
            AddPart(_sign, "Lettering", BuildTextMesh(aspect), FieldMaterials.Additive(text), ShaderIds.PaletteAccent, 2.4f,
                true);
            _sign.gameObject.SetActive(false);
        }

        /// <summary>
        /// <paramref name="intensity"/>: the lettering's (additive) or, for a solid part, its edge intensity (&lt; 0 keeps
        /// the material's).
        /// </summary>
        private static void AddPart(Transform parent, string name, Mesh mesh, Material material, Vector4 palette,
            float intensity, bool additive)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            var block = new MaterialPropertyBlock();
            FieldMaterials.SetPalette(block, palette);
            if (additive)
            {
                block.SetFloat(ShaderIds.Intensity, intensity);
                block.SetFloat(ShaderIds.FogAmount, 0.85f);
                FieldMaterials.GuardAdditive(r);
            }
            else if (intensity >= 0f)
            {
                block.SetFloat(ShaderIds.EdgeIntensity, intensity);
            }

            r.SetPropertyBlock(block);
        }

        private static Mesh BuildBoardMesh()
        {
            var b = new MeshBuilder();
            MeshFactory.AddBox(b, new Vector3(0f, PlateY, 0f), new Vector3(BoardHalfWidth, PlateHalfHeight, PlateHalfDepth),
                MeshFactory.AllFaces);
            return RenderCache.Track(b.ToMesh("BeamRunner Sign"));
        }

        /// <summary>Two posts from under the board down to the beam's face (dim edges: they hold the sign up, nothing more).</summary>
        private static Mesh BuildPostsMesh()
        {
            var b = new MeshBuilder();
            float top = PlateY - PlateHalfHeight;
            float bottom = FieldMath.FloorY;
            for (int s = -1; s <= 1; s += 2)
            {
                MeshFactory.AddBox(b, new Vector3(s * (BoardHalfWidth - PostInset), (top + bottom) * 0.5f, PlateHalfDepth * 0.5f),
                    new Vector3(0.14f, (top - bottom) * 0.5f, 0.08f), MeshFactory.AllFaces);
            }

            return RenderCache.Track(b.ToMesh("BeamRunner Sign Posts"));
        }

        private static Mesh BuildTextMesh(float aspect)
        {
            float w;
            float h;
            Fit(BoardHalfWidth * 2f - 0.5f, PlateHalfHeight * 2f - 0.3f, aspect, out w, out h);
            var c = new Vector3(0f, PlateY, -PlateHalfDepth - 0.03f);
            var positions = new List<Vector3>
            {
                c + new Vector3(-w * 0.5f, -h * 0.5f, 0f),
                c + new Vector3(w * 0.5f, -h * 0.5f, 0f),
                c + new Vector3(-w * 0.5f, h * 0.5f, 0f),
                c + new Vector3(w * 0.5f, h * 0.5f, 0f),
            };
            var uvs = new List<Vector2> { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
            var colors = new List<Color32>
            {
                new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255),
                new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255),
            };
            var mesh = new Mesh { name = "BeamRunner Sign Lettering" };
            mesh.SetVertices(positions);
            mesh.SetUVs(0, uvs);
            mesh.SetColors(colors);
            mesh.SetTriangles(new[] { 0, 2, 1, 1, 2, 3 }, 0, true);
            return RenderCache.Track(mesh);
        }

        /// <summary>Largest width × height of the given aspect inside a box.</summary>
        private static void Fit(float maxW, float maxH, float aspect, out float w, out float h)
        {
            w = maxW;
            h = w / aspect;
            if (h > maxH)
            {
                h = maxH;
                w = h * aspect;
            }
        }

        // ------------------------------------------------------------------ the lettering (a block pixel font)

        private const int GlyphRows = 7;
        private const int Cell = 12;
        private const int PadCells = 2;

        /// <summary>
        /// <see cref="Brand"/> as glowing square dots (each font pixel one little block): white in RGB, the shape in alpha,
        /// so the Additive shader tints it. <paramref name="aspect"/> = width / height.
        /// </summary>
        private static Texture2D BrandTexture(out float aspect)
        {
            // Lay the glyphs out on a dot grid, trimmed to their lit columns, one blank column between letters.
            var columns = new List<bool[]>(80);
            for (int c = 0; c < Brand.Length; c++)
            {
                string[] g = Glyph(Brand[c]);
                if (g == null)
                {
                    for (int s = 0; s < 3; s++)
                    {
                        columns.Add(new bool[GlyphRows]);
                    }
                }
                else
                {
                    int min = 5;
                    int max = -1;
                    for (int row = 0; row < GlyphRows; row++)
                    {
                        for (int x = 0; x < 5; x++)
                        {
                            if (Lit(g, row, x))
                            {
                                min = Mathf.Min(min, x);
                                max = Mathf.Max(max, x);
                            }
                        }
                    }

                    for (int x = min; x <= max; x++)
                    {
                        var col = new bool[GlyphRows];
                        for (int row = 0; row < GlyphRows; row++)
                        {
                            col[row] = Lit(g, row, x);
                        }

                        columns.Add(col);
                    }
                }

                if (c + 1 < Brand.Length)
                {
                    columns.Add(new bool[GlyphRows]);
                }
            }

            int gridW = columns.Count + PadCells * 2;
            int gridH = GlyphRows + PadCells * 2;
            int w = gridW * Cell;
            int h = gridH * Cell;
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                float fy = y + 0.5f;
                int cy = y / Cell;
                for (int x = 0; x < w; x++)
                {
                    float fx = x + 0.5f;
                    int cx = x / Cell;
                    float near = float.MaxValue;
                    for (int gy = cy - 2; gy <= cy + 2; gy++)
                    {
                        int row = GlyphRows - 1 - (gy - PadCells); // texture y runs up, glyph rows run down
                        if (row < 0 || row >= GlyphRows)
                        {
                            continue;
                        }

                        for (int gx = cx - 2; gx <= cx + 2; gx++)
                        {
                            int col = gx - PadCells;
                            if (col < 0 || col >= columns.Count || !columns[col][row])
                            {
                                continue;
                            }

                            // Distance (texels) to this dot's square, inset one texel inside its cell.
                            float dx = Mathf.Max(0f, Mathf.Max(gx * Cell + 1f - fx, fx - (gx * Cell + Cell - 1f)));
                            float dy = Mathf.Max(0f, Mathf.Max(gy * Cell + 1f - fy, fy - (gy * Cell + Cell - 1f)));
                            near = Mathf.Min(near, Mathf.Sqrt(dx * dx + dy * dy));
                        }
                    }

                    float a = 0f;
                    if (near < float.MaxValue)
                    {
                        a = Mathf.Max(Mathf.Clamp01(1f - near), 0.42f * Mathf.Exp(-near / (Cell * 0.45f)));
                    }

                    px[y * w + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            }

            aspect = (float)w / h;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true, true);
            tex.name = "BeamRunner Brand Lettering";
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Trilinear;
            tex.anisoLevel = 4;
            tex.SetPixels32(px);
            tex.Apply(true, true);
            return RenderCache.Track(tex);
        }

        private static bool Lit(string[] glyph, int row, int x)
        {
            return row < glyph.Length && glyph[row][x] == '#';
        }

        /// <summary>
        /// 5 × 7 dot glyphs for the letters of <see cref="Brand"/> (others draw as a space). Add the letters a new name
        /// needs here.
        /// </summary>
        private static string[] Glyph(char c)
        {
            switch (c)
            {
                case 'A':
                    return new[] { " ### ", "#   #", "#   #", "#####", "#   #", "#   #", "#   #" };
                case 'B':
                    return new[] { "#### ", "#   #", "#   #", "#### ", "#   #", "#   #", "#### " };
                case 'E':
                    return new[] { "#####", "#    ", "#    ", "#### ", "#    ", "#    ", "#####" };
                case 'M':
                    return new[] { "#   #", "## ##", "# # #", "# # #", "#   #", "#   #", "#   #" };
                case 'N':
                    return new[] { "#   #", "##  #", "# # #", "#  ##", "#   #", "#   #", "#   #" };
                case 'R':
                    return new[] { "#### ", "#   #", "#   #", "#### ", "# #  ", "#  # ", "#   #" };
                case 'U':
                    return new[] { "#   #", "#   #", "#   #", "#   #", "#   #", "#   #", " ### " };
                default:
                    return null;
            }
        }
    }
}

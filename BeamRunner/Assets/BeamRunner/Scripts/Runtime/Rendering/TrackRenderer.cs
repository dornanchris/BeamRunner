using System.Collections.Generic;
using BeamRunner.Core.Field;
using BeamRunner.Core.Sim;
using UnityEngine;
using UnityEngine.Rendering;

namespace BeamRunner.Rendering
{
    /// <summary>
    /// Draws the rail grid of a <see cref="RunSimulation"/>: every rail of light (one per lane per layer), ramps, the
    /// tile ticks, freshly built pieces snapping in with a glow, the ghost outline where the next build will land, the
    /// warning on a gap ahead and the hazard line of the ceiling — all in one dynamic additive mesh rebuilt each frame
    /// (one draw call, no allocation in steady state) — plus pooled neon cubes for obstacles and the wall that ends each
    /// level. Rails take the level palette by layer (bottom = secondary, middle = primary, top = accent); the layer the
    /// craft rides is brightest, the layer below it dimmest (what is under you is hard to see).
    /// </summary>
    public sealed class TrackRenderer : MonoBehaviour
    {
        /// <summary>Seconds a built piece takes to snap in.</summary>
        public const float SnapSeconds = 0.2f;

        private const int MaxQuads = 3000;
        private const float RailWidth = 0.6f;
        private const float FinHeight = 0.34f;
        private const float GhostHalfWidth = 0.95f;
        private const float WallHalfWidth = 5.6f;
        private const float WallBottom = -14f;

        private readonly Vector3[] _v = new Vector3[MaxQuads * 4];
        private readonly Vector2[] _uv = new Vector2[MaxQuads * 4];
        private readonly Color32[] _c = new Color32[MaxQuads * 4];
        private readonly int[] _tri = new int[MaxQuads * 6];
        private int _quads;

        private readonly Dictionary<long, MeshRenderer> _cubes = new Dictionary<long, MeshRenderer>(32);
        private readonly Dictionary<long, int> _cubeStamp = new Dictionary<long, int>(32);
        private readonly Stack<MeshRenderer> _cubePool = new Stack<MeshRenderer>(32);
        private readonly Dictionary<long, MeshRenderer> _walls = new Dictionary<long, MeshRenderer>(4);
        private readonly Stack<MeshRenderer> _wallPool = new Stack<MeshRenderer>(4);
        private readonly List<long> _stale = new List<long>(16);
        private MaterialPropertyBlock _cubeBlock;
        private MaterialPropertyBlock _wallBlock;
        private Transform _cubeRoot;
        private int _frame;

        private Mesh _mesh;
        private MeshRenderer _renderer;
        private FxLibrary _fx;
        private float _time;

        /// <summary>Obstacle cubes shown now (debug).</summary>
        public int ActiveObstacleCount
        {
            get { return _cubes.Count; }
        }

        public static TrackRenderer Create(Transform parent, FxLibrary fx)
        {
            var go = new GameObject("Track");
            go.transform.SetParent(parent, false);
            var r = go.AddComponent<TrackRenderer>();
            r._fx = fx;
            r.Build();
            return r;
        }

        private void Build()
        {
            _mesh = new Mesh { name = "BeamRunner Rails" };
            _mesh.MarkDynamic();
            gameObject.AddComponent<MeshFilter>().sharedMesh = _mesh;
            _renderer = gameObject.AddComponent<MeshRenderer>();
            _renderer.sharedMaterial = FieldMaterials.Beam();
            Configure(_renderer);
            var block = new MaterialPropertyBlock();
            block.SetVector(ShaderIds.Palette, ShaderIds.PaletteCustom);
            block.SetColor(ShaderIds.TintColor, Color.white);
            block.SetFloat(ShaderIds.Intensity, 3.6f);
            block.SetFloat(ShaderIds.FogAmount, 0.85f);
            _renderer.SetPropertyBlock(block);
            FieldMaterials.GuardAdditive(_renderer);

            for (int i = 0; i < MaxQuads; i++)
            {
                int v = i * 4;
                int t = i * 6;
                _tri[t] = v;
                _tri[t + 1] = v + 1;
                _tri[t + 2] = v + 2;
                _tri[t + 3] = v + 2;
                _tri[t + 4] = v + 1;
                _tri[t + 5] = v + 3;
            }

            _mesh.bounds = new Bounds(new Vector3(0f, 0f, 90f), new Vector3(30f, 40f, 220f));

            _cubeBlock = new MaterialPropertyBlock();
            _wallBlock = new MaterialPropertyBlock();
            var root = new GameObject("Obstacles");
            root.transform.SetParent(transform, false);
            _cubeRoot = root.transform;
        }

        /// <summary>Hides everything (hangar, between runs).</summary>
        public void Clear()
        {
            _quads = 0;
            Flush();
            foreach (KeyValuePair<long, MeshRenderer> kv in _cubes)
            {
                kv.Value.gameObject.SetActive(false);
                _cubePool.Push(kv.Value);
            }

            _cubes.Clear();
            _cubeStamp.Clear();
            foreach (KeyValuePair<long, MeshRenderer> kv in _walls)
            {
                kv.Value.gameObject.SetActive(false);
                _wallPool.Push(kv.Value);
            }

            _walls.Clear();
        }

        /// <summary>An obstacle broke (Overload, a shield): shatter FX at its world position.</summary>
        public void OnSmashed(Vector3 world, int layer)
        {
            if (_fx != null)
            {
                _fx.SmashBurst(world, LayerColor(layer));
            }
        }

        /// <summary>A piece snapped in: a spark burst at its start (world position).</summary>
        public void OnBuilt(Vector3 world, int layer)
        {
            if (_fx != null)
            {
                _fx.NearMissSpark(world, Color.Lerp(LayerColor(layer), Color.white, 0.5f));
            }
        }

        /// <summary>Rebuilds the track for this frame from the simulation's grid.</summary>
        public void Sync(RunSimulation sim, bool showGhost)
        {
            _frame++;
            _quads = 0;
            _time += Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            if (sim == null)
            {
                Flush();
                ReleaseUnstamped();
                return;
            }

            TrackGrid grid = sim.Grid;
            double s = sim.Distance;
            long first = System.Math.Max(grid.FirstTile, sim.Tile - 2);
            long last = System.Math.Min(grid.EndTile - 1, FieldSpec.TileOf(s + FieldMath.VisibleLength));
            int shipLayer = sim.Phase == RunPhase.Riding || sim.Phase == RunPhase.Falling ? sim.Layer : FieldSpec.MiddleLayer;
            float now = (float)sim.Time;

            for (long t = first; t <= last; t++)
            {
                TrackRow row = grid.Row(t);
                if (row == null)
                {
                    continue;
                }

                float z0 = FieldMath.WorldZ(FieldSpec.TileStart(t), s);
                float z1 = z0 + FieldSpec.TileLength;
                if (row.IsWall)
                {
                    PlaceWall(t, z0);
                    continue;
                }

                for (int layer = 0; layer < FieldSpec.LayerCount; layer++)
                {
                    Color layerColor = LayerColor(layer) * LayerBrightness(layer, shipLayer);
                    float y = FieldSpec.RailY(layer);
                    for (int lane = 0; lane < FieldSpec.LaneCount; lane++)
                    {
                        CellType c = row.Get(lane, layer);
                        if (c == CellType.Empty)
                        {
                            continue;
                        }

                        float x = FieldSpec.LaneX(lane);
                        Color color = layerColor;
                        float length = 1f;
                        int i = FieldSpec.CellIndex(lane, layer);
                        float builtAt = row.BuiltAt[i];
                        if (builtAt >= 0f)
                        {
                            float age = Mathf.Max(0f, now - builtAt);
                            float snap = Mathf.Clamp01(age / SnapSeconds);
                            length = 1f - (1f - snap) * (1f - snap) * (1f - snap);
                            color = Color.Lerp(layerColor, Color.white, 0.45f) * (1.25f + 2.2f * (1f - snap));
                        }

                        float dy = Cells.LayerDelta(c) * FieldSpec.LayerSpacing;
                        var a = new Vector3(x, y, z0);
                        var b = new Vector3(x, y + dy * length, z0 + FieldSpec.TileLength * length);
                        Rail(a, b, color);
                        if (dy == 0f)
                        {
                            Tick(x, y, z0, color * 0.55f);
                        }

                        if (c == CellType.Obstacle)
                        {
                            PlaceCube(t, i, lane, layer, z0);
                        }
                    }
                }
            }

            Ceiling(first, last, s, shipLayer);
            if (showGhost && !sim.IsCrashed)
            {
                Ghost(sim, s);
            }

            Flush();
            ReleaseUnstamped();
        }

        // ------------------------------------------------------------------ pieces

        private void Rail(Vector3 a, Vector3 b, Color color)
        {
            Color32 c = ToVertex(color);
            float w = RailWidth * 0.5f;
            // Flat ribbon (seen from above) and a vertical fin (seen edge-on from layers above or below).
            Quad(new Vector3(a.x - w, a.y, a.z), new Vector3(a.x + w, a.y, a.z), new Vector3(b.x - w, b.y, b.z),
                new Vector3(b.x + w, b.y, b.z), c);
            float f = FinHeight * 0.5f;
            Quad(new Vector3(a.x, a.y - f, a.z), new Vector3(a.x, a.y + f, a.z), new Vector3(b.x, b.y - f, b.z),
                new Vector3(b.x, b.y + f, b.z), c);
        }

        /// <summary>A short crossbar at a tile's start: the tile rhythm a build snaps to.</summary>
        private void Tick(float x, float y, float z, Color color)
        {
            Color32 c = ToVertex(color);
            const float half = 0.55f;
            const float w = 0.09f;
            Quad(new Vector3(x - half, y, z - w), new Vector3(x - half, y, z + w), new Vector3(x + half, y, z - w),
                new Vector3(x + half, y, z + w), c);
        }

        /// <summary>The ceiling: a red hazard line over each lane with stripes, brightest from the top layer.</summary>
        private void Ceiling(long first, long last, double s, int shipLayer)
        {
            float k = shipLayer >= FieldSpec.TopLayer ? 1f : shipLayer == FieldSpec.MiddleLayer ? 0.35f : 0.15f;
            float pulse = 0.85f + 0.15f * Mathf.Sin(_time * 4f);
            Color hazard = new Color(1f, 0.16f, 0.22f) * (k * pulse);
            Color32 c = ToVertex(hazard);
            Color32 dim = ToVertex(hazard * 0.6f);
            float y = FieldSpec.CeilingY;
            float z0 = FieldMath.WorldZ(FieldSpec.TileStart(first), s);
            float z1 = FieldMath.WorldZ(FieldSpec.TileStart(last + 1), s);
            for (int lane = 0; lane < FieldSpec.LaneCount; lane++)
            {
                float x = FieldSpec.LaneX(lane);
                Quad(new Vector3(x - 0.2f, y, z0), new Vector3(x + 0.2f, y, z0), new Vector3(x - 0.2f, y, z1),
                    new Vector3(x + 0.2f, y, z1), c);
            }

            // Diagonal hazard stripes, one per tile, across the whole width.
            for (long t = first; t <= last; t++)
            {
                float z = FieldMath.WorldZ(FieldSpec.TileStart(t), s);
                Quad(new Vector3(-FieldSpec.HalfExtent, y, z), new Vector3(-FieldSpec.HalfExtent, y, z + 0.5f),
                    new Vector3(FieldSpec.HalfExtent, y, z + 2.2f), new Vector3(FieldSpec.HalfExtent, y, z + 2.7f), dim);
            }
        }

        /// <summary>The ghost outline on the build target, and the warning on the next gap ahead.</summary>
        private void Ghost(RunSimulation sim, double s)
        {
            long tile;
            int lane;
            int layer;
            float pulse = 0.5f + 0.5f * Mathf.Sin(_time * 10f);
            if (sim.BuildTarget(out tile, out lane, out layer))
            {
                CellType c = sim.Grid.Get(tile, lane, layer);
                bool gap = c == CellType.Empty;
                bool broke = !sim.CanAfford(BuildKind.Bridge) && !sim.CanAfford(BuildKind.RampDown);
                Color color = broke ? new Color(1f, 0.2f, 0.2f) * 0.8f
                    : gap ? Color.Lerp(FieldGlobals.AccentColor, Color.white, 0.4f) * (0.9f + 0.9f * pulse)
                    : new Color(1f, 1f, 1f) * 0.32f;
                Outline(tile, lane, layer, s, color, gap ? 0.16f : 0.1f);
            }

            long ahead = sim.GapAhead();
            if (ahead > tile && ahead >= 0)
            {
                // The layer the craft will be on there: follow the ramps from the target layer.
                int l = layer;
                for (long t = tile; t < ahead; t++)
                {
                    l += Cells.LayerDelta(sim.Grid.Get(t, sim.TargetLane, l));
                }

                if (l >= 0 && l < FieldSpec.LayerCount)
                {
                    Color warn = new Color(1f, 0.55f, 0.1f) * (0.45f + 0.5f * pulse);
                    Outline(ahead, sim.TargetLane, l, s, warn, 0.1f);
                }
            }
        }

        private void Outline(long tile, int lane, int layer, double s, Color color, float width)
        {
            Color32 c = ToVertex(color);
            float x = FieldSpec.LaneX(lane);
            float y = FieldSpec.RailY(layer) + 0.04f;
            float z0 = FieldMath.WorldZ(FieldSpec.TileStart(tile), s) + 0.15f;
            float z1 = z0 + FieldSpec.TileLength - 0.3f;
            float h = GhostHalfWidth;
            float w = width * 0.5f;
            Quad(new Vector3(x - h - w, y, z0), new Vector3(x - h + w, y, z0), new Vector3(x - h - w, y, z1),
                new Vector3(x - h + w, y, z1), c);
            Quad(new Vector3(x + h - w, y, z0), new Vector3(x + h + w, y, z0), new Vector3(x + h - w, y, z1),
                new Vector3(x + h + w, y, z1), c);
            Quad(new Vector3(x - h, y, z0 - w), new Vector3(x - h, y, z0 + w), new Vector3(x + h, y, z0 - w),
                new Vector3(x + h, y, z0 + w), c);
            Quad(new Vector3(x - h, y, z1 - w), new Vector3(x - h, y, z1 + w), new Vector3(x + h, y, z1 - w),
                new Vector3(x + h, y, z1 + w), c);
        }

        private void Quad(Vector3 a0, Vector3 a1, Vector3 b0, Vector3 b1, Color32 c)
        {
            if (_quads >= MaxQuads)
            {
                return;
            }

            int v = _quads * 4;
            _v[v] = a0;
            _v[v + 1] = a1;
            _v[v + 2] = b0;
            _v[v + 3] = b1;
            _uv[v] = new Vector2(0f, 0f);
            _uv[v + 1] = new Vector2(0f, 1f);
            _uv[v + 2] = new Vector2(1f, 0f);
            _uv[v + 3] = new Vector2(1f, 1f);
            _c[v] = c;
            _c[v + 1] = c;
            _c[v + 2] = c;
            _c[v + 3] = c;
            _quads++;
        }

        /// <summary>Uploads only the quads drawn this frame (indices first dropped, so the vertex count can shrink).</summary>
        private void Flush()
        {
            int n = _quads * 4;
            const MeshUpdateFlags flags = MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices;
            _mesh.SetIndices(_tri, 0, 0, MeshTopology.Triangles, 0, false);
            _mesh.SetVertices(_v, 0, n, flags);
            _mesh.SetUVs(0, _uv, 0, n, flags);
            _mesh.SetColors(_c, 0, n, flags);
            _mesh.SetIndices(_tri, 0, _quads * 6, MeshTopology.Triangles, 0, false);
            _renderer.enabled = _quads > 0;
        }

        // ------------------------------------------------------------------ cubes and walls

        private void PlaceCube(long tile, int cell, int lane, int layer, float z0)
        {
            long key = tile * FieldSpec.CellsPerRow + cell;
            MeshRenderer r;
            if (!_cubes.TryGetValue(key, out r))
            {
                r = _cubePool.Count > 0 ? _cubePool.Pop() : NewCube();
                r.gameObject.SetActive(true);
                _cubeBlock.Clear();
                FieldMaterials.SetPalette(_cubeBlock, ShaderIds.PaletteSlot(LayerSlot(layer)));
                r.SetPropertyBlock(_cubeBlock);
                _cubes.Add(key, r);
            }

            _cubeStamp[key] = _frame;
            float z = z0 + FieldSpec.TileLength * 0.5f;
            Transform tr = r.transform;
            tr.localPosition = new Vector3(FieldSpec.LaneX(lane), FieldSpec.RailY(layer) + FieldSpec.ObstacleHalfSize, z);

            // Shrink away once passed, before the chase camera reaches it.
            float scale = Mathf.Clamp01((z + 3.5f) / 3f);
            tr.localScale = Vector3.one * scale;
            r.enabled = scale > 0.01f;
        }

        private void PlaceWall(long tile, float z0)
        {
            MeshRenderer r;
            if (!_walls.TryGetValue(tile, out r))
            {
                r = _wallPool.Count > 0 ? _wallPool.Pop() : NewWall();
                r.gameObject.SetActive(true);
                _walls.Add(tile, r);
            }

            _cubeStamp[-1 - tile] = _frame;
            float height = RunSimulation.WallTopY - WallBottom;
            r.transform.localPosition = new Vector3(0f, WallBottom + height * 0.5f, z0 + 0.6f);
            _wallBlock.Clear();
            FieldMaterials.SetPalette(_wallBlock, ShaderIds.PaletteAccent);
            r.SetPropertyBlock(_wallBlock);

            // Rungs: the ship is carried up the wall's face.
            Color rung = Color.Lerp(FieldGlobals.AccentColor, Color.white, 0.3f) * (0.8f + 0.4f * Mathf.Sin(_time * 6f));
            Color32 c = ToVertex(rung);
            for (int k = -1; k <= FieldSpec.LayerCount; k++)
            {
                float y = FieldSpec.RailY(k) + 1.5f;
                Quad(new Vector3(-WallHalfWidth, y - 0.08f, z0), new Vector3(-WallHalfWidth, y + 0.08f, z0),
                    new Vector3(WallHalfWidth, y - 0.08f, z0), new Vector3(WallHalfWidth, y + 0.08f, z0), c);
            }
        }

        private void ReleaseUnstamped()
        {
            _stale.Clear();
            foreach (KeyValuePair<long, MeshRenderer> kv in _cubes)
            {
                int stamp;
                if (!_cubeStamp.TryGetValue(kv.Key, out stamp) || stamp != _frame)
                {
                    _stale.Add(kv.Key);
                }
            }

            for (int i = 0; i < _stale.Count; i++)
            {
                MeshRenderer r = _cubes[_stale[i]];
                r.gameObject.SetActive(false);
                _cubePool.Push(r);
                _cubes.Remove(_stale[i]);
                _cubeStamp.Remove(_stale[i]);
            }

            _stale.Clear();
            foreach (KeyValuePair<long, MeshRenderer> kv in _walls)
            {
                int stamp;
                if (!_cubeStamp.TryGetValue(-1 - kv.Key, out stamp) || stamp != _frame)
                {
                    _stale.Add(kv.Key);
                }
            }

            for (int i = 0; i < _stale.Count; i++)
            {
                MeshRenderer r = _walls[_stale[i]];
                r.gameObject.SetActive(false);
                _wallPool.Push(r);
                _walls.Remove(_stale[i]);
                _cubeStamp.Remove(-1 - _stale[i]);
            }
        }

        private MeshRenderer NewCube()
        {
            var go = new GameObject("Obstacle");
            go.transform.SetParent(_cubeRoot, false);
            go.AddComponent<MeshFilter>().sharedMesh = MeshFactory.Cube(FieldSpec.ObstacleHalfSize);
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = FieldMaterials.Obstacle();
            Configure(r);
            return r;
        }

        private MeshRenderer NewWall()
        {
            var go = new GameObject("Wall");
            go.transform.SetParent(_cubeRoot, false);
            float height = RunSimulation.WallTopY - WallBottom;
            go.AddComponent<MeshFilter>().sharedMesh = MeshFactory.Box(new Vector3(WallHalfWidth, height * 0.5f, 0.6f));
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = FieldMaterials.Obstacle();
            Configure(r);
            return r;
        }

        // ------------------------------------------------------------------ colours

        /// <summary>Palette slot of a layer: bottom = secondary, middle = primary, top = accent.</summary>
        public static int LayerSlot(int layer)
        {
            return layer <= 0 ? 1 : layer == 1 ? 0 : 2;
        }

        /// <summary>Neon colour of a layer's rails (sRGB, level palette).</summary>
        public static Color LayerColor(int layer)
        {
            return FieldGlobals.PaletteColor(LayerSlot(layer));
        }

        private static float LayerBrightness(int layer, int shipLayer)
        {
            if (layer == shipLayer)
            {
                return 1f;
            }

            return layer > shipLayer ? 0.5f : 0.3f;
        }

        private static Color32 ToVertex(Color srgb)
        {
            // HDR-ish intensities above 1 are folded into the material intensity range by clamping per channel.
            Color c = FieldGlobals.ToShaderSpace(new Color(Mathf.Clamp01(srgb.r * 0.7f), Mathf.Clamp01(srgb.g * 0.7f),
                Mathf.Clamp01(srgb.b * 0.7f), 1f));
            return new Color32((byte)(Mathf.Clamp01(c.r) * 255f), (byte)(Mathf.Clamp01(c.g) * 255f),
                (byte)(Mathf.Clamp01(c.b) * 255f), 255);
        }

        private static void Configure(MeshRenderer r)
        {
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }
    }
}

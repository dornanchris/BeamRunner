using System.Collections.Generic;
using BeamRunner.Core;
using BeamRunner.Core.Field;
using BeamRunner.Core.Sim;
using UnityEngine;
using UnityEngine.Rendering;

namespace BeamRunner.Rendering
{
    /// <summary>
    /// Draws the rail grid of a <see cref="RunSimulation"/>: every rail of light (one per lane per layer), ramps, the
    /// tile ticks, freshly built pieces snapping in with a glow, the ghost outline where the next build will land, the
    /// warning on a gap ahead, the hazard line of the ceiling and, at the end of each level, the bend where every rail
    /// turns up the wall into the next level — all in one dynamic additive mesh rebuilt each frame (one draw call, no
    /// allocation in steady state) in <see cref="TrackSpace"/> — plus pooled neon cubes for obstacles. Rails take the
    /// level palette by layer (bottom = secondary, middle = primary, top = accent; the next level's rows up the wall
    /// already wear its palette); the layer the craft rides is brightest, and the layer above it fades out overhead so it
    /// never crowds the view of the layer the craft rides and the one below.
    /// </summary>
    public sealed class TrackRenderer : MonoBehaviour
    {
        /// <summary>Seconds a built piece takes to snap in.</summary>
        public const float SnapSeconds = 0.2f;

        private const int MaxQuads = 3000;
        private const float RailWidth = 0.6f;
        private const float FinHeight = 0.34f;
        private const float GhostHalfWidth = 0.95f;

        /// <summary>Pieces a rail is split into where the track bends, and a switch's diagonal.</summary>
        private const int BendPieces = 8;

        private const int SwitchPieces = 6;

        // The arrows beside a ramp: how many along it, how far out from the rail and how high over it (m).
        private const int RampArrowCount = 3;
        private const float RampArrowOffset = 0.95f;
        private const float RampArrowLift = 0.4f;

        // The layer above the craft dims to AboveFadeFloor overhead and is full from AboveFadeEnd metres ahead: on the
        // single route it is usually where the rail goes next, so it must stay readable near the craft too.
        private const float AboveFadeStart = 2f;
        private const float AboveFadeEnd = 18f;
        private const float AboveFadeFloor = 0.45f;


        private readonly Vector3[] _v = new Vector3[MaxQuads * 4];
        private readonly Vector2[] _uv = new Vector2[MaxQuads * 4];
        private readonly Color32[] _c = new Color32[MaxQuads * 4];
        private readonly int[] _tri = new int[MaxQuads * 6];
        private int _quads;

        private readonly Dictionary<long, MeshRenderer> _cubes = new Dictionary<long, MeshRenderer>(32);
        private readonly Dictionary<long, int> _cubeStamp = new Dictionary<long, int>(32);
        private readonly Stack<MeshRenderer> _cubePool = new Stack<MeshRenderer>(32);
        private readonly List<long> _stale = new List<long>(16);
        private MaterialPropertyBlock _cubeBlock;
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

            // Bend-root space: the track ahead (≤ VisibleLength) may run flat, or up the wall, or come down behind.
            _mesh.bounds = new Bounds(new Vector3(0f, 90f, 90f), new Vector3(30f, 440f, 440f));

            _cubeBlock = new MaterialPropertyBlock();
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

        /// <summary>Rebuilds the track for this frame from the simulation's grid (in <see cref="TrackSpace"/>, set first).</summary>
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
            // On the bend the craft drifts to the middle layer: brighten whichever layer it is nearest.
            int shipLayer = sim.IsClimbing
                ? Mathf.Clamp(Mathf.RoundToInt((sim.ShipY - FieldSpec.RideY(0)) / FieldSpec.LayerSpacing), 0, FieldSpec.TopLayer)
                : sim.Layer;
            float now = (float)sim.Time;
            int frameLevel = sim.Level;
            ZoneTheme next = ZoneThemes.ForLevel(frameLevel + 1);

            for (long t = first; t <= last; t++)
            {
                TrackRow row = grid.Row(t);
                if (row == null)
                {
                    continue;
                }

                double s0 = FieldSpec.TileStart(t);
                if (row.IsWall)
                {
                    WallBend(s0, s, shipLayer);
                    continue;
                }

                // The next level's rows (up the wall ahead) already wear its palette.
                bool upcoming = row.Level > frameLevel;
                for (int layer = 0; layer < FieldSpec.LayerCount; layer++)
                {
                    Color layerColor = (upcoming ? ThemeLayerColor(next, layer) : LayerColor(layer)) *
                                       LayerBrightness(layer, shipLayer);
                    float y = FieldSpec.RailY(layer);
                    bool above = layer > shipLayer && !upcoming;
                    for (int lane = 0; lane < FieldSpec.LaneCount; lane++)
                    {
                        CellType c = row.Get(lane, layer);
                        if (c == CellType.Empty)
                        {
                            continue;
                        }

                        float x = FieldSpec.LaneX(lane);

                        // A ramp wears the colour of the layer it leads to (and carries arrows pointing there).
                        int toLayer = layer + Cells.LayerDelta(c);
                        Color baseColor = toLayer == layer || toLayer < 0 || toLayer >= FieldSpec.LayerCount
                            ? layerColor
                            : (upcoming ? ThemeLayerColor(next, toLayer) : LayerColor(toLayer)) *
                              Mathf.Max(LayerBrightness(layer, shipLayer), LayerBrightness(toLayer, shipLayer));
                        Color color = baseColor;
                        float length = 1f;
                        int i = FieldSpec.CellIndex(lane, layer);
                        float builtAt = row.BuiltAt[i];
                        if (builtAt >= 0f)
                        {
                            float age = Mathf.Max(0f, now - builtAt);
                            float snap = Mathf.Clamp01(age / SnapSeconds);
                            length = 1f - (1f - snap) * (1f - snap) * (1f - snap);
                            color = Color.Lerp(baseColor, Color.white, 0.45f) * (1.25f + 2.2f * (1f - snap));
                        }

                        float dy = Cells.LayerDelta(c) * FieldSpec.LayerSpacing;
                        float dx = Cells.LaneDelta(c) * FieldSpec.CellSize; // a switch: the diagonal into the next lane
                        double s1 = s0 + FieldSpec.TileLength * length;
                        Color c0 = above ? color * AboveFade(s0 - s) : color;
                        Color c1 = above ? color * AboveFade(s1 - s) : color;
                        Rail(x, dx, length, y, y + dy * length, s0, s1, c0, c1);
                        if (dy == 0f && dx == 0f && layer == shipLayer)
                        {
                            Tick(x, y, s0, c0 * 0.35f); // the tile rhythm, on the craft's own layer only
                        }
                        else if (dy != 0f && length >= 1f)
                        {
                            RampArrows(x, y, dy, s0, c0, c1);
                        }

                        // Where a rail stops (no rail goes on in its lane at the height it leaves at) and starts: a
                        // bright bar, so the end of the line reads from far away.
                        if (length >= 1f && c != CellType.Obstacle)
                        {
                            int exit = layer + Cells.LayerDelta(c);
                            TrackRow after = grid.Row(t + 1);
                            if (after != null && !after.IsSolidAt(lane + Cells.LaneDelta(c), exit))
                            {
                                Cap(x + dx, y + dy, s1, c1);
                            }

                            TrackRow before = grid.Row(t - 1);
                            if (before != null && !before.IsWall && !LeadsInto(before, lane, layer))
                            {
                                Cap(x, y, s0, c0);
                            }
                        }

                        if (c == CellType.Obstacle)
                        {
                            PlaceCube(t, i, lane, layer, s0);
                        }
                    }
                }
            }

            Ceiling(first, last, s, shipLayer);
            if (showGhost && !sim.IsCrashed)
            {
                Ghost(sim);
            }

            Flush();
            ReleaseUnstamped();
        }

        // ------------------------------------------------------------------ pieces

        /// <summary>
        /// A rail from (<paramref name="y0"/>, <paramref name="s0"/>) to (<paramref name="y1"/>, <paramref name="s1"/>)
        /// in lane <paramref name="x"/>: a flat ribbon (seen from above) and a vertical fin (seen edge-on from layers above
        /// or below), split into short pieces where the track bends. A switch curves <paramref name="dx"/> sideways over
        /// its whole tile (the same S the craft rides); <paramref name="span"/> is the part of the tile drawn so far
        /// while it snaps in.
        /// </summary>
        private void Rail(float x, float dx, float span, float y0, float y1, double s0, double s1, Color c0, Color c1)
        {
            int pieces = TrackSpace.IsBent(s0, s1) ? BendPieces : dx != 0f ? SwitchPieces : 1;
            float w = RailWidth * 0.5f;
            float f = FinHeight * 0.5f;
            for (int k = 0; k < pieces; k++)
            {
                float ta = k / (float)pieces;
                float tb = (k + 1) / (float)pieces;
                double sa = s0 + (s1 - s0) * ta;
                double sb = s0 + (s1 - s0) * tb;
                float ya = y0 + (y1 - y0) * ta;
                float yb = y0 + (y1 - y0) * tb;
                float xa = x + dx * MathUtil.SmoothStep(ta * span);
                float xb = x + dx * MathUtil.SmoothStep(tb * span);
                Color32 ca = ToVertex(Color.Lerp(c0, c1, ta));
                Color32 cb = ToVertex(Color.Lerp(c0, c1, tb));
                Quad(TrackSpace.Map(xa - w, ya, sa), TrackSpace.Map(xa + w, ya, sa), TrackSpace.Map(xb - w, yb, sb),
                    TrackSpace.Map(xb + w, yb, sb), ca, cb);
                Quad(TrackSpace.Map(xa, ya - f, sa), TrackSpace.Map(xa, ya + f, sa), TrackSpace.Map(xb, yb - f, sb),
                    TrackSpace.Map(xb, yb + f, sb), ca, cb);
            }
        }

        /// <summary>
        /// Chevrons standing beside a ramp on both sides, pointing the way it goes (up or down), lit in turn in the
        /// direction of travel: a ramp reads as a ramp from far away, and which way it leads at a glance.
        /// </summary>
        private void RampArrows(float x, float y, float dy, double s0, Color c0, Color c1)
        {
            float dir = Mathf.Sign(dy);
            const float halfWidth = 0.36f;
            const float halfHeight = 0.2f;
            const float stroke = 0.08f;
            for (int k = 0; k < RampArrowCount; k++)
            {
                float along = (k + 0.5f) / RampArrowCount;
                double sk = s0 + FieldSpec.TileLength * along;
                float yk = y + dy * along + RampArrowLift;
                float wave = Mathf.Repeat(along - _time * 1.6f, 1f);
                Color color = Color.Lerp(c0, c1, along) * (0.9f + 1.4f * wave * wave);
                Color32 c = ToVertex(Color.Lerp(color, Color.white, 0.2f));
                for (int side = -1; side <= 1; side += 2)
                {
                    float cx = x + side * RampArrowOffset;
                    float tipY = yk + dir * halfHeight;
                    float footY = yk - dir * halfHeight;
                    Stroke(cx - halfWidth, footY, cx, tipY, sk, stroke, c);
                    Stroke(cx, tipY, cx + halfWidth, footY, sk, stroke, c);
                }
            }
        }

        /// <summary>A line of width <paramref name="w"/> in the X-Y plane at track position <paramref name="s"/> (it faces the camera).</summary>
        private void Stroke(float x0, float y0, float x1, float y1, double s, float w, Color32 c)
        {
            float dx = x1 - x0;
            float dy = y1 - y0;
            float len = Mathf.Max(1e-4f, Mathf.Sqrt(dx * dx + dy * dy));
            float nx = -dy / len * w * 0.5f;
            float ny = dx / len * w * 0.5f;
            Quad(TrackSpace.Map(x0 - nx, y0 - ny, s), TrackSpace.Map(x0 + nx, y0 + ny, s),
                TrackSpace.Map(x1 - nx, y1 - ny, s), TrackSpace.Map(x1 + nx, y1 + ny, s), c, c);
        }

        /// <summary>A bright bar across the end (or start) of a rail.</summary>
        private void Cap(float x, float y, double s, Color color)
        {
            Color32 c = ToVertex(Color.Lerp(color, Color.white, 0.25f) * 1.15f);
            const float half = 0.75f;
            const double w = 0.14;
            Quad(TrackSpace.Map(x - half, y, s - w), TrackSpace.Map(x - half, y, s + w), TrackSpace.Map(x + half, y, s - w),
                TrackSpace.Map(x + half, y, s + w), c, c);
        }

        /// <summary>True if a cell of <paramref name="row"/> rides into (<paramref name="lane"/>, <paramref name="layer"/>) of the next tile.</summary>
        private static bool LeadsInto(TrackRow row, int lane, int layer)
        {
            for (int from = layer - 1; from <= layer + 1; from++)
            {
                for (int side = lane - 1; side <= lane + 1; side++)
                {
                    CellType c = row.Get(side, from);
                    if (c != CellType.Empty && c != CellType.Obstacle && from + Cells.LayerDelta(c) == layer &&
                        side + Cells.LaneDelta(c) == lane)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>A short crossbar at a tile's start: the tile rhythm a build snaps to.</summary>
        private void Tick(float x, float y, double s, Color color)
        {
            Color32 c = ToVertex(color);
            const float half = 0.55f;
            const double w = 0.09;
            Quad(TrackSpace.Map(x - half, y, s - w), TrackSpace.Map(x - half, y, s + w), TrackSpace.Map(x + half, y, s - w),
                TrackSpace.Map(x + half, y, s + w), c, c);
        }

        /// <summary>
        /// The wall tile: every rail of every layer curves up the wall (the bend the craft rides into the next level),
        /// with a bright threshold bar across each layer where the turn begins.
        /// </summary>
        private void WallBend(double s0, double s, int shipLayer)
        {
            double s1 = s0 + FieldSpec.TileLength;
            float pulse = 0.8f + 0.4f * Mathf.Sin(_time * 6f);
            for (int layer = 0; layer < FieldSpec.LayerCount; layer++)
            {
                Color color = LayerColor(layer) * LayerBrightness(layer, shipLayer);
                Color c0 = layer > shipLayer ? color * AboveFade(s0 - s) : color;
                Color c1 = layer > shipLayer ? color * AboveFade(s1 - s) : color;
                float y = FieldSpec.RailY(layer);
                for (int lane = 0; lane < FieldSpec.LaneCount; lane++)
                {
                    Rail(FieldSpec.LaneX(lane), 0f, 1f, y, y, s0, s1, c0, c1);
                }

                Color32 bar = ToVertex(Color.Lerp(LayerColor(layer), Color.white, 0.35f) * (pulse * (layer > shipLayer ? AboveFade(s0 - s) : 1f)));
                float h = FieldSpec.HalfExtent;
                Quad(TrackSpace.Map(-h, y, s0 - 0.15), TrackSpace.Map(-h, y, s0 + 0.15), TrackSpace.Map(h, y, s0 - 0.15),
                    TrackSpace.Map(h, y, s0 + 0.15), bar, bar);
            }
        }

        /// <summary>The ceiling: a red hazard line over each lane with stripes, brightest from the top layer.</summary>
        private void Ceiling(long first, long last, double s, int shipLayer)
        {
            float k = shipLayer >= FieldSpec.TopLayer ? 1f : shipLayer == FieldSpec.MiddleLayer ? 0.18f : 0.08f;
            float pulse = 0.85f + 0.15f * Mathf.Sin(_time * 4f);
            Color hazard = new Color(1f, 0.16f, 0.22f) * (k * pulse);
            Color32 dim = ToVertex(hazard * 0.6f);
            float y = FieldSpec.CeilingY;
            for (long t = first; t <= last; t++)
            {
                double s0 = FieldSpec.TileStart(t);
                double s1 = s0 + FieldSpec.TileLength;
                Color c0 = shipLayer < FieldSpec.TopLayer ? hazard * AboveFade(s0 - s) : hazard;
                Color c1 = shipLayer < FieldSpec.TopLayer ? hazard * AboveFade(s1 - s) : hazard;
                for (int lane = 0; lane < FieldSpec.LaneCount; lane++)
                {
                    Strip(FieldSpec.LaneX(lane), y, 0.2f, s0, s1, c0, c1);
                }

                // A diagonal hazard stripe per tile across the whole width (straight tiles, from the top layer only).
                if (shipLayer >= FieldSpec.TopLayer && !TrackSpace.IsBent(s0, s1))
                {
                    float h = FieldSpec.HalfExtent;
                    Quad(TrackSpace.Map(-h, y, s0), TrackSpace.Map(-h, y, s0 + 0.5), TrackSpace.Map(h, y, s0 + 2.2),
                        TrackSpace.Map(h, y, s0 + 2.7), dim, dim);
                }
            }
        }

        /// <summary>A flat strip of half width <paramref name="w"/> at height <paramref name="y"/>, bent with the track.</summary>
        private void Strip(float x, float y, float w, double s0, double s1, Color c0, Color c1)
        {
            int pieces = TrackSpace.IsBent(s0, s1) ? BendPieces : 1;
            for (int k = 0; k < pieces; k++)
            {
                float ta = k / (float)pieces;
                float tb = (k + 1) / (float)pieces;
                double sa = s0 + (s1 - s0) * ta;
                double sb = s0 + (s1 - s0) * tb;
                Quad(TrackSpace.Map(x - w, y, sa), TrackSpace.Map(x + w, y, sa), TrackSpace.Map(x - w, y, sb),
                    TrackSpace.Map(x + w, y, sb), ToVertex(Color.Lerp(c0, c1, ta)), ToVertex(Color.Lerp(c0, c1, tb)));
            }
        }

        /// <summary>The ghost outline on the build target, and the warning on the next gap ahead.</summary>
        private void Ghost(RunSimulation sim)
        {
            long tile;
            int lane;
            int layer;
            float pulse = 0.5f + 0.5f * Mathf.Sin(_time * 10f);
            if (sim.BuildTarget(out tile, out lane, out layer))
            {
                CellType c = sim.Grid.Get(tile, lane, layer);
                bool gap = c == CellType.Empty;
                int climb = gap ? Continuation(sim.Grid, tile + 1, lane, layer) : 0;
                BuildKind needed = climb > 0 ? BuildKind.RampUp : climb < 0 ? BuildKind.RampDown : BuildKind.Bridge;
                bool broke = !sim.CanAfford(needed) && !sim.CanAfford(BuildKind.RampDown);
                Color color = broke ? new Color(1f, 0.2f, 0.2f) * 0.8f
                    : gap ? Color.Lerp(FieldGlobals.AccentColor, Color.white, 0.4f) * (0.9f + 0.9f * pulse)
                    : new Color(1f, 1f, 1f) * 0.32f;
                Outline(tile, lane, layer, climb, color, gap ? 0.16f : 0.1f);
                if (climb != 0)
                {
                    // Where the ramp lands: the rail that goes on a layer up or down.
                    Outline(tile + 1, lane, layer + climb, 0, color * 0.6f, 0.1f);
                }
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
                    // Up, down or across: the warning already takes the shape of the build the gap needs.
                    Color warn = new Color(1f, 0.55f, 0.1f) * (0.45f + 0.5f * pulse);
                    Outline(ahead, sim.TargetLane, l, Continuation(sim.Grid, ahead + 1, sim.TargetLane, l), warn, 0.1f);
                }
            }
        }

        /// <summary>
        /// Which way the rail goes on at <paramref name="tile"/> in <paramref name="lane"/> for a craft on
        /// <paramref name="layer"/>: 0 on the same layer (or nowhere: a bridge is all that can help), +1 only a layer up,
        /// −1 only a layer down.
        /// </summary>
        private static int Continuation(TrackGrid grid, long tile, int lane, int layer)
        {
            TrackRow row = grid.Row(tile);
            if (row == null || row.IsWall || row.IsSolidAt(lane, layer))
            {
                return 0;
            }

            if (row.IsSolidAt(lane, layer + 1))
            {
                return 1;
            }

            return row.IsSolidAt(lane, layer - 1) ? -1 : 0;
        }

        /// <summary>
        /// The outline of a tile on <paramref name="layer"/>, flat or (<paramref name="climb"/> ±1) as the ramp that
        /// would take the craft a layer up or down across it.
        /// </summary>
        private void Outline(long tile, int lane, int layer, int climb, Color color, float width)
        {
            Color32 c = ToVertex(color);
            float x = FieldSpec.LaneX(lane);
            float y0 = FieldSpec.RailY(layer) + 0.04f;
            float y1 = y0 + climb * FieldSpec.LayerSpacing;
            double z0 = FieldSpec.TileStart(tile) + 0.15;
            double z1 = z0 + FieldSpec.TileLength - 0.3;
            float h = GhostHalfWidth;
            float w = width * 0.5f;
            Quad(TrackSpace.Map(x - h - w, y0, z0), TrackSpace.Map(x - h + w, y0, z0), TrackSpace.Map(x - h - w, y1, z1),
                TrackSpace.Map(x - h + w, y1, z1), c, c);
            Quad(TrackSpace.Map(x + h - w, y0, z0), TrackSpace.Map(x + h + w, y0, z0), TrackSpace.Map(x + h - w, y1, z1),
                TrackSpace.Map(x + h + w, y1, z1), c, c);
            Quad(TrackSpace.Map(x - h, y0, z0 - w), TrackSpace.Map(x - h, y0, z0 + w), TrackSpace.Map(x + h, y0, z0 - w),
                TrackSpace.Map(x + h, y0, z0 + w), c, c);
            Quad(TrackSpace.Map(x - h, y1, z1 - w), TrackSpace.Map(x - h, y1, z1 + w), TrackSpace.Map(x + h, y1, z1 - w),
                TrackSpace.Map(x + h, y1, z1 + w), c, c);
            if (climb != 0)
            {
                // A spine up the middle of the ramp, so its slope reads at a glance.
                Quad(TrackSpace.Map(x - w, y0, z0), TrackSpace.Map(x + w, y0, z0), TrackSpace.Map(x - w, y1, z1),
                    TrackSpace.Map(x + w, y1, z1), c, c);
            }
        }

        // a0/a1 take colour ca, b0/b1 colour cb.
        private void Quad(Vector3 a0, Vector3 a1, Vector3 b0, Vector3 b1, Color32 ca, Color32 cb)
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
            _c[v] = ca;
            _c[v + 1] = ca;
            _c[v + 2] = cb;
            _c[v + 3] = cb;
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

        // ------------------------------------------------------------------ cubes

        private void PlaceCube(long tile, int cell, int lane, int layer, double s0)
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
            double mid = s0 + FieldSpec.TileLength * 0.5;
            Vector3 p = TrackSpace.Map(FieldSpec.LaneX(lane), FieldSpec.RailY(layer) + FieldSpec.ObstacleHalfSize, mid);
            Transform tr = r.transform;
            tr.localPosition = p;
            tr.localRotation = TrackSpace.Tilt(mid);

            // Shrink away once passed, before the chase camera reaches it.
            float scale = Mathf.Clamp01((p.z + 3.5f) / 3f);
            tr.localScale = Vector3.one * scale;
            r.enabled = scale > 0.01f;
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

        /// <summary>Colour of a layer's rails in another level's palette (the next level's rails up the wall).</summary>
        private static Color ThemeLayerColor(ZoneTheme theme, int layer)
        {
            int slot = LayerSlot(layer);
            return FieldGlobals.ToColor(slot == 1 ? theme.Secondary : slot == 2 ? theme.Accent : theme.Primary);
        }

        private static float LayerBrightness(int layer, int shipLayer)
        {
            if (layer == shipLayer)
            {
                return 1f;
            }

            // The other layers are where the route goes on (and where a miss lands): they must read clearly.
            return layer > shipLayer ? 0.6f : 0.65f;
        }

        /// <summary>
        /// Brightness of the layer above the craft by how far ahead (m) it is: barely there overhead, where it would cut
        /// across the view, full from a few tiles ahead, where the pilot plans ramps up to it.
        /// </summary>
        private static float AboveFade(double ahead)
        {
            float t = Mathf.Clamp01(((float)ahead - AboveFadeStart) / (AboveFadeEnd - AboveFadeStart));
            return AboveFadeFloor + (1f - AboveFadeFloor) * t * t * (3f - 2f * t);
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

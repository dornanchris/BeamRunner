using System;
using System.Collections.Generic;
using BeamRunner.Core.Balance;

namespace BeamRunner.Core.Field
{
    /// <summary>
    /// Lays the track ahead of the craft, tile by tile, into a <see cref="TrackGrid"/> and queues its pickups. Every
    /// level is one forced <b>route</b>: <see cref="Levels.IntroTiles"/> of track on all three lanes of the middle layer
    /// (the craft arrives over the wall in any lane), of which only the route's lane goes on; then
    /// <see cref="RouteBeats"/> — gaps, lane shifts, ramps to build, drops, cube dodges, forks — joined by a tile or two of
    /// the route rail alone; then a runway on the route's layer and the closing wall. A beat is placed only if the route
    /// survives it: a <see cref="RouteSolver"/> frontier (the most fuel an optimal pilot can hold on each lane and layer)
    /// is carried along, and a beat that leaves the route without fuel to reach its end is skipped — low on fuel, fuel
    /// beats are favoured. Pickups (only ever on rails) and optional obstacles are rolled by the level and the adaptive
    /// modifiers (fuel, obstacles, drops, gems); power-ups keep their own schedule. Deterministic from the seed;
    /// pickups are queued in Z order.
    /// </summary>
    public sealed class TrackGenerator
    {
        /// <summary>Shortest gem line that pays the full-trail bonus.</summary>
        public const int MinTrailGems = 4;

        /// <summary>Below this much (optimal) fuel, fuel chunks are strongly favoured.</summary>
        public const float LowFuel = 6f;

        /// <summary>Height of gems and fuel cells above their rail (high enough that the rail shows beneath them).</summary>
        public const float PickupHeight = 1.2f;

        private const int MaxPicks = 8;

        private readonly Course _course;
        private readonly bool _tutorial;
        private readonly DifficultyModifiers _mods;
        private readonly BalanceConfig _balance;
        private readonly ShipStats _ship;
        private readonly TrackGrid _grid;
        private readonly List<Pickup> _out;
        private readonly RouteSolver _solver;
        private readonly Rng _layout;
        private readonly Rng _items;
        private readonly Rng _power;
        private readonly float[] _frontier = new float[RouteSolver.StateCount];
        private readonly float[] _tmpA = new float[RouteSolver.StateCount];
        private readonly float[] _tmpB = new float[RouteSolver.StateCount];
        private readonly List<BeatKind> _candidates = new List<BeatKind>(16);
        private readonly List<float> _weights = new List<float>(64);
        private readonly List<Pickup> _rowPickups = new List<Pickup>(16);
        private readonly List<Pickup>[] _trails = { new List<Pickup>(), new List<Pickup>(), new List<Pickup>() };
        private readonly RouteSolver.CellReader _gridCell;
        private readonly RouteSolver.FuelReader _gridFuel;
        private readonly RouteSolver.CellReader _chunkCell;
        private readonly RouteSolver.FuelReader _chunkFuel;

        /// <summary>Rows of clear track a pickup needs after it (see <see cref="InClearSight"/>).</summary>
        public const int SightTiles = 2;

        private const byte WantGems = 0;
        private const byte WantFuel = 1;
        private const byte WantPowerUp = 2;

        private struct Wanted
        {
            public long Tile;
            public byte What;
            public PickupKind Kind;
            public int Lane;
            public int Layer;
            public CellType Cell;
            public bool Scheduled;
        }

        // Pickups asked for by rows not yet finalized (in tile order), and the next row to finalize: the frontier
        // stands at its start.
        private readonly List<Wanted> _wants = new List<Wanted>(64);
        private long _finalized;

        // The chunk being evaluated: its rows as cells and fuel, before they are committed.
        private readonly List<CellType[]> _pendingCells = new List<CellType[]>(16);
        private readonly List<float[]> _pendingFuel = new List<float[]>(16);
        private readonly List<char[]> _pendingCodes = new List<char[]>(16);
        private long _pendingStart;

        private LevelSpec _spec;
        private long _levelStart = long.MinValue;
        private long _wallTile;
        private int _fillerLeft;
        private BeatKind _lastBeat = (BeatKind)(-1);
        private int _routeLane = FieldSpec.CenterLane;
        private int _routeLayer = FieldSpec.MiddleLayer;

        // The branch rail beside the route (see OverlayBranch).
        private bool _branchAlive;
        private int _branchLane;
        private int _branchLayer;
        private int _branchLeft;
        private bool _branchGapped;
        private double _nextPowerUpTile;
        private int _nextPickupId = 1;
        private int _nextTrailId = 1;
        private double _gemRemainder;
        private bool _firstLevel = true;

        public TrackGenerator(int seed, Course course, bool tutorial, DifficultyModifiers mods, BalanceConfig balance,
            ShipStats ship, TrackGrid grid, List<Pickup> pickupsOut)
        {
            _course = course;
            _tutorial = tutorial;
            _mods = mods ?? DifficultyModifiers.Standard;
            _balance = balance ?? BalanceConfig.Current;
            _ship = ship ?? ShipStats.FromLevels(null, _balance);
            _grid = grid;
            _out = pickupsOut;
            _layout = new Rng(seed);
            _items = new Rng(seed ^ 0x5bd1e995);
            _power = new Rng(seed ^ 0x27d4eb2f);
            _solver = new RouteSolver
            {
                BridgeCost = _ship.BridgeCost,
                RampUpCost = _ship.RampUpCost,
                RampDownCost = _ship.RampDownCost,
                MaxFuel = _ship.MaxFuel,
            };
            _gridCell = GridCell;
            _gridFuel = GridFuel;
            _chunkCell = ChunkCell;
            _chunkFuel = ChunkFuel;
            _nextPowerUpTile = PowerUpGapTiles(course.StartLevel) * 0.6;
        }

        /// <summary>The solver the generator plans with (same prices as the run's craft).</summary>
        public RouteSolver Solver
        {
            get { return _solver; }
        }

        /// <summary>Most fuel an optimal pilot can hold entering the next generated tile (planning, tests).</summary>
        public float FrontierBest
        {
            get { return RouteSolver.Best(_frontier); }
        }

        /// <summary>Next free pickup id (shared with pickups the simulation spawns).</summary>
        public int NextPickupId()
        {
            return _nextPickupId++;
        }

        /// <summary>Generates every tile before <paramref name="tile"/> (run tiles).</summary>
        public void GenerateUntil(long tile)
        {
            int guard = 0;
            while (_grid.EndTile < tile && guard++ < 10000)
            {
                GenerateNext();
            }
        }

        // ------------------------------------------------------------------ rows

        private void GenerateNext()
        {
            long t = _grid.EndTile;
            if (_spec == null || t >= _wallTile + 1)
            {
                StartLevel(_course.LevelOfTile(t), t);
            }

            long rel = t - _levelStart;
            int intro = Levels.IntroTiles + (_firstLevel ? 2 : 0);
            if (t == _wallTile)
            {
                WriteWall();
                return;
            }

            if (rel < intro)
            {
                WriteOpen(FieldSpec.MiddleLayer);
                return;
            }

            if (t >= _wallTile - Levels.RunwayTiles)
            {
                WriteOpen(_routeLayer); // any lane rides into the wall
                return;
            }

            if (_fillerLeft > 0)
            {
                _fillerLeft--;
                WriteFiller(true);
                return;
            }

            if (!TryPlaceBeat(t))
            {
                WriteFiller(true);
            }
        }

        private void StartLevel(int level, long startTile)
        {
            bool first = _spec == null;
            _spec = Levels.Get(level);
            _firstLevel = first;
            _levelStart = _course.LevelStartTile(level);
            _wallTile = _course.WallTile(level);
            if (startTile != _levelStart)
            {
                // Only when a run is resumed mid-level (never in practice); keep the plan consistent.
                _levelStart = startTile;
            }

            _fillerLeft = 0;
            _lastBeat = (BeatKind)(-1);
            _routeLane = _layout.Range(0, FieldSpec.LaneCount);
            _routeLayer = FieldSpec.MiddleLayer;
            _branchAlive = false;
            _solver.MaxLayer = _spec.TopLayer ? FieldSpec.TopLayer : FieldSpec.MiddleLayer;

            // The craft starts every level on the middle layer, in any lane, with whatever fuel it carried over the wall
            // (later levels: reseeded when the frontier, which runs SightTiles behind, finalizes the wall).
            if (first)
            {
                Reseed(_frontier, _ship.StartFuel);
            }
        }

        /// <summary>The frontier at a level's start: the middle layer, any lane, with <paramref name="fuel"/>.</summary>
        private static void Reseed(float[] frontier, float fuel)
        {
            RouteSolver.Clear(frontier);
            for (int lane = 0; lane < FieldSpec.LaneCount; lane++)
            {
                frontier[RouteSolver.State(lane, FieldSpec.MiddleLayer, BuildKind.None)] = Math.Max(0f, fuel);
            }
        }

        private void WriteWall()
        {
            TrackRow row = _grid.Append(_spec.Number);
            row.IsWall = true;
            for (int i = 0; i < FieldSpec.CellsPerRow; i++)
            {
                row.Cells[i] = CellType.Wall;
            }

            RowDone();
        }

        /// <summary>All three lanes of one layer (the level's intro, the runway into the wall).</summary>
        private void WriteOpen(int layer)
        {
            TrackRow row = _grid.Append(_spec.Number);
            for (int lane = 0; lane < FieldSpec.LaneCount; lane++)
            {
                row.Set(lane, layer, CellType.Track);
            }

            RowDone();
        }

        /// <summary>The route rail alone; between beats it may carry fuel, a gem line or a power-up.</summary>
        private void WriteFiller(bool items)
        {
            TrackRow row = _grid.Append(_spec.Number);
            row.Set(_routeLane, _routeLayer, CellType.Track);
            OverlayBranch(row);
            if (items)
            {
                if (_items.Chance(_balance.FillerFuelChance * _spec.FuelScale * _mods.Fuel))
                {
                    Want(row, WantFuel, PickupKind.Fuel, _routeLane, _routeLayer, CellType.Track);
                }
                else if (_items.Chance(0.35f))
                {
                    Want(row, WantGems, PickupKind.Gem, _routeLane, _routeLayer, CellType.Track);
                }

                if (row.Tile >= _nextPowerUpTile)
                {
                    WantScheduledPowerUp(row, _routeLane, _routeLayer, CellType.Track);
                }
            }

            RowDone();
        }

        // ------------------------------------------------------------------ beats

        private bool TryPlaceBeat(long t)
        {
            long room = _wallTile - Levels.RunwayTiles - t;
            int maxTier = _tutorial && _spec.Number <= 2 ? 0 : _spec.MaxChunkTier;
            int maxLayer = _spec.TopLayer ? FieldSpec.TopLayer : FieldSpec.MiddleLayer;
            float netChance = _tutorial || _spec.Number <= 2 ? 1f : MathUtil.Lerp(0.6f, 0.15f, _spec.Tier);
            bool low = RouteSolver.Best(_frontier) < LowFuel;
            _candidates.Clear();
            _weights.Clear();
            BeatKind[] all = RouteBeats.All;
            for (int i = 0; i < all.Length; i++)
            {
                BeatKind k = all[i];
                ChunkTag tag = RouteBeats.TagOf(k);
                if (RouteBeats.TierOf(k) > maxTier || (k == _lastBeat && tag != ChunkTag.Fuel) ||
                    (tag == ChunkTag.Obstacle && !_spec.Obstacles))
                {
                    continue;
                }

                float w = _spec.TagWeights[(int)tag] * (1f + 0.4f * RouteBeats.TierOf(k));
                if (tag == ChunkTag.Obstacle)
                {
                    w *= _mods.Obstacles;
                }

                if (tag == ChunkTag.Fuel && low)
                {
                    w = Math.Max(w, 0.5f) * 4f;
                }

                if (w > 0f)
                {
                    _candidates.Add(k);
                    _weights.Add(w);
                }
            }

            for (int attempt = 0; attempt < MaxPicks && _candidates.Count > 0; attempt++)
            {
                int pick = Pick();
                BeatKind kind = _candidates[pick];
                Beat beat = RouteBeats.Build(kind, _routeLane, _routeLayer, maxLayer, maxTier, netChance, _layout);
                if (beat != null && beat.Chunk.Length <= room)
                {
                    Stage(beat.Chunk, false, t);
                    if (Affordable(t, beat.EndLane, beat.EndLayer))
                    {
                        Commit(beat.Chunk);
                        _lastBeat = kind;
                        _routeLane = beat.EndLane;
                        _routeLayer = beat.EndLayer;
                        float gap = _spec.ChunkGap / Math.Max(0.3f, _mods.Gaps);
                        if (_tutorial && _spec.Number <= 2)
                        {
                            gap *= 2f;
                        }

                        _fillerLeft = Math.Max(0, (int)Math.Round(gap - 1f + _layout.Range(0f, 1f)));
                        return true;
                    }
                }

                _candidates.RemoveAt(pick);
                _weights.RemoveAt(pick);
            }

            return false;
        }

        private int Pick()
        {
            float total = 0f;
            for (int i = 0; i < _weights.Count; i++)
            {
                total += _weights[i];
            }

            float r = _layout.Range(0f, total);
            for (int i = 0; i < _weights.Count; i++)
            {
                r -= _weights[i];
                if (r <= 0f)
                {
                    return i;
                }
            }

            return _weights.Count - 1;
        }

        /// <summary>Rolls a chunk's optional obstacles and fuel into the pending buffer (not committed yet).</summary>
        private void Stage(Chunk chunk, bool mirror, long start)
        {
            _pendingStart = start;
            _pendingCells.Clear();
            _pendingFuel.Clear();
            _pendingCodes.Clear();
            float keepFuel = _spec.FuelScale * _mods.Fuel;
            float obstacleRoll = 0.5f * _mods.Obstacles;
            for (int r = 0; r < chunk.Length; r++)
            {
                var cells = new CellType[FieldSpec.CellsPerRow];
                var fuel = new float[FieldSpec.CellsPerRow];
                var codes = new char[FieldSpec.CellsPerRow];
                for (int layer = 0; layer < FieldSpec.LayerCount; layer++)
                {
                    for (int lane = 0; lane < FieldSpec.LaneCount; lane++)
                    {
                        int i = FieldSpec.CellIndex(lane, layer);
                        char code = chunk.Code(r, lane, layer, mirror);
                        bool roll = code == 'o' && _spec.Obstacles && _layout.Chance(obstacleRoll);
                        cells[i] = Chunk.CellOf(code, roll);
                        if (Chunk.HoldsFuel(code))
                        {
                            if (keepFuel >= 1f || _items.Chance(keepFuel))
                            {
                                fuel[i] = _balance.FuelPickupValue;
                            }
                            else
                            {
                                code = cells[i] == CellType.Empty ? '.' : '=';
                            }
                        }

                        codes[i] = code;
                    }
                }

                _pendingCells.Add(cells);
                _pendingFuel.Add(fuel);
                _pendingCodes.Add(codes);
            }

            // Only fuel the beat itself shows in clear sight is sure to be placed: count no other in the check.
            for (int r = 0; r < _pendingCells.Count; r++)
            {
                float[] fuel = _pendingFuel[r];
                for (int i = 0; i < fuel.Length; i++)
                {
                    if (fuel[i] > 0f && !PendingClearSight(r, i % FieldSpec.LaneCount, i / FieldSpec.LaneCount))
                    {
                        fuel[i] = 0f;
                    }
                }
            }
        }

        private bool PendingClearSight(int r, int lane, int layer)
        {
            int y = layer + Cells.LayerDelta(_pendingCells[r][FieldSpec.CellIndex(lane, layer)]);
            for (int k = 1; k <= SightTiles; k++)
            {
                if (r + k >= _pendingCells.Count || y < 0 || y >= FieldSpec.LayerCount)
                {
                    return false;
                }

                CellType c = _pendingCells[r + k][FieldSpec.CellIndex(lane, y)];
                if (c != CellType.Track && c != CellType.RampUp)
                {
                    return false;
                }

                y += Cells.LayerDelta(c);
            }

            return true;
        }

        /// <summary>True if the route survives the staged beat from the current frontier, ending on its exit rail.</summary>
        private bool Affordable(long start, int endLane, int endLayer)
        {
            Array.Copy(_frontier, _tmpA, _tmpA.Length);
            float[] from = _tmpA;
            float[] to = _tmpB;

            // The rows written but not finalized yet: their pickups may still be dropped, so count none of their fuel.
            for (long t = _finalized; t < start; t++)
            {
                _solver.Step(t, from, to, _gridCell, null);
                float[] swap = from;
                from = to;
                to = swap;
                TrackRow row = _grid.Row(t);
                if (row != null && row.IsWall)
                {
                    Reseed(from, RouteSolver.Best(from));
                }
            }

            for (int r = 0; r < _pendingCells.Count; r++)
            {
                _solver.Step(start + r, from, to, _chunkCell, _chunkFuel);
                float[] swap = from;
                from = to;
                to = swap;
            }

            return from[RouteSolver.State(endLane, endLayer, BuildKind.None)] >= 0f;
        }

        private void Commit(Chunk chunk)
        {
            int powerSlots = 0;
            for (int r = 0; r < _pendingCells.Count; r++)
            {
                TrackRow row = _grid.Append(_spec.Number);
                CellType[] cells = _pendingCells[r];
                char[] codes = _pendingCodes[r];
                Array.Copy(cells, row.Cells, cells.Length);
                for (int layer = 0; layer < FieldSpec.LayerCount; layer++)
                {
                    for (int lane = 0; lane < FieldSpec.LaneCount; lane++)
                    {
                        int i = FieldSpec.CellIndex(lane, layer);
                        char code = codes[i];
                        CellType cell = cells[i];
                        switch (code)
                        {
                            case 'g':
                                Want(row, WantGems, PickupKind.Gem, lane, layer, cell);
                                break;
                            case 'f':
                                Want(row, WantFuel, PickupKind.Fuel, lane, layer, cell);
                                break;
                            case 's':
                                Want(row, WantPowerUp, PickupKind.Shield, lane, layer, cell);
                                break;
                            case 'r':
                                Want(row, WantPowerUp, PickupKind.Repair, lane, layer, cell);
                                break;
                            case 'p':
                                if (row.Tile >= _nextPowerUpTile && powerSlots == 0)
                                {
                                    powerSlots++;
                                    WantScheduledPowerUp(row, lane, layer, cell);
                                }
                                else
                                {
                                    Want(row, WantGems, PickupKind.Gem, lane, layer, cell);
                                }

                                break;
                        }
                    }
                }

                OverlayBranch(row);
                RowDone();
            }
        }

        // ------------------------------------------------------------------ branches

        /// <summary>
        /// A second rail beside the route, laid over its rows (only into gaps, so the route is never changed and the
        /// fairness check made without it still holds): it branches off next to the route — a lane over or a layer up or
        /// down — runs 5–14 tiles with an occasional gap, lane drift or ramp to another layer, often carries gems, then
        /// ends. Lane switches lay their own diagonals, so it is a real alternative: hop over to it, ride it, hop back.
        /// </summary>
        private void OverlayBranch(TrackRow row)
        {
            int maxLayer = _spec.TopLayer ? FieldSpec.TopLayer : FieldSpec.MiddleLayer;
            if (!_branchAlive)
            {
                float spawn = _spec.Number <= 1 ? 0.12f : 0.3f;
                if (!_items.Chance(spawn))
                {
                    return;
                }

                int lane = _routeLane;
                int layer = _routeLayer;
                if (_items.Chance(0.6f))
                {
                    lane += lane <= 0 ? 1 : lane >= FieldSpec.LaneCount - 1 ? -1 : (_items.Chance(0.5f) ? -1 : 1);
                }
                else
                {
                    layer += layer <= 0 ? 1 : layer >= maxLayer ? -1 : (_items.Chance(0.5f) ? -1 : 1);
                }

                if (layer < 0 || layer > maxLayer || row.Get(lane, layer) != CellType.Empty)
                {
                    return;
                }

                _branchAlive = true;
                _branchLane = lane;
                _branchLayer = layer;
                _branchLeft = _items.Range(5, 15);
                _branchGapped = true; // no gap on its first tile
            }

            CellType here = row.Get(_branchLane, _branchLayer);
            if (here == CellType.Obstacle || --_branchLeft <= 0)
            {
                _branchAlive = false;
                return;
            }

            if (!_branchGapped && _items.Chance(0.12f))
            {
                _branchGapped = true; // a gap in the branch: bridge it or hop across
                return;
            }

            _branchGapped = false;
            if (here != CellType.Empty)
            {
                return; // it meets the route here
            }

            float roll = _items.Range(0f, 1f);
            if (roll < 0.1f && _branchLayer < maxLayer && row.Get(_branchLane, _branchLayer + 1) == CellType.Empty)
            {
                row.Set(_branchLane, _branchLayer, CellType.RampUp);
                _branchLayer++;
                return;
            }

            if (roll < 0.2f && _branchLayer > 0 && row.Get(_branchLane, _branchLayer - 1) == CellType.Empty)
            {
                row.Set(_branchLane, _branchLayer, CellType.RampDown);
                _branchLayer--;
                return;
            }

            row.Set(_branchLane, _branchLayer, CellType.Track);
            if (_items.Chance(0.3f))
            {
                Want(row, WantGems, PickupKind.Gem, _branchLane, _branchLayer, CellType.Track);
            }

            if (roll > 0.88f)
            {
                // Drift to the next lane: the rail goes on there from the next tile.
                int to = _branchLane + (_branchLane <= 0 ? 1 : _branchLane >= FieldSpec.LaneCount - 1 ? -1 : (_items.Chance(0.5f) ? -1 : 1));
                _branchLane = to;
            }
        }

        // ------------------------------------------------------------------ pickups (decided once the track ahead is known)

        /// <summary>A pickup a row asks for; it is placed only if the track goes on in clear sight behind it.</summary>
        private void Want(TrackRow row, byte what, PickupKind kind, int lane, int layer, CellType cell, bool scheduled = false)
        {
            _wants.Add(new Wanted { Tile = row.Tile, What = what, Kind = kind, Lane = lane, Layer = layer, Cell = cell, Scheduled = scheduled });
        }

        /// <summary>The power-up the schedule is due (the schedule moves on now; a dropped one is due again at once).</summary>
        private void WantScheduledPowerUp(TrackRow row, int lane, int layer, CellType cell)
        {
            Want(row, WantPowerUp, NextPowerUp(), lane, layer, cell, true);
            _nextPowerUpTile = row.Tile + PowerUpGapTiles(_spec.Number);
        }

        /// <summary>A row is written: finalize every row that now has <see cref="SightTiles"/> rows after it.</summary>
        private void RowDone()
        {
            while (_finalized <= _grid.EndTile - 1 - SightTiles)
            {
                Finalize(_finalized++);
            }
        }

        /// <summary>
        /// Places a row's pickups (those in clear sight of the track ahead), then carries the frontier over it — with its
        /// real fuel — and, past a wall, reseeds it at the next level's start.
        /// </summary>
        private void Finalize(long tile)
        {
            TrackRow row = _grid.Row(tile);
            _rowPickups.Clear();
            int n = 0;
            while (n < _wants.Count && _wants[n].Tile == tile)
            {
                Wanted w = _wants[n++];
                if (!InClearSight(tile, w.Lane, w.Layer))
                {
                    if (w.Scheduled)
                    {
                        _nextPowerUpTile = 0; // due again at the next spot
                    }

                    continue;
                }

                switch (w.What)
                {
                    case WantGems:
                        AddGems(row, w.Lane, w.Layer, w.Cell);
                        break;
                    case WantFuel:
                        AddFuel(row, w.Lane, w.Layer, w.Cell);
                        break;
                    default:
                        AddPowerUp(row, w.Lane, w.Layer, w.Cell, w.Kind);
                        break;
                }
            }

            _wants.RemoveRange(0, n);
            CommitRowPickups(row);
            Advance(tile);
            if (row.IsWall)
            {
                CloseTrails(true);
                Reseed(_frontier, RouteSolver.Best(_frontier));
            }
        }

        /// <summary>
        /// True if the craft riding on from (<paramref name="tile"/>, <paramref name="lane"/>, <paramref name="layer"/>)
        /// meets rail (or a ramp rising into view) for the next <see cref="SightTiles"/> tiles, or the wall: a pickup
        /// there never stands in front of a gap, a drop, a ramp down or a cube, where it would hide that the track stops.
        /// </summary>
        private bool InClearSight(long tile, int lane, int layer)
        {
            int y = layer + Cells.LayerDelta(_grid.Get(tile, lane, layer));
            for (int k = 1; k <= SightTiles; k++)
            {
                TrackRow r = _grid.Row(tile + k);
                if (r == null)
                {
                    return false;
                }

                if (r.IsWall)
                {
                    return true;
                }

                CellType c = r.Get(lane, y);
                if (c != CellType.Track && c != CellType.RampUp)
                {
                    return false;
                }

                y += Cells.LayerDelta(c);
            }

            return true;
        }

        private void AddGems(TrackRow row, int lane, int layer, CellType cell)
        {
            int n = _balance.GemsPerCell;
            double value = _balance.GemValue(_spec.Number) * _mods.Gems;
            for (int k = 0; k < n; k++)
            {
                float f = (k + 0.5f) / n;
                // Stochastic rounding keeps the average payout exact.
                double v = value + _gemRemainder;
                int whole = Math.Max(1, (int)Math.Floor(v));
                _gemRemainder = v - whole;
                var p = NewPickup(PickupKind.Gem, row, lane, layer, cell, f);
                p.Value = whole;
                _trails[lane].Add(p);
            }

            row.Gems[FieldSpec.CellIndex(lane, layer)] = (byte)Math.Min(255, row.Gems[FieldSpec.CellIndex(lane, layer)] + n);
        }

        private void AddFuel(TrackRow row, int lane, int layer, CellType cell)
        {
            var p = NewPickup(PickupKind.Fuel, row, lane, layer, cell, 0.5f);
            p.Value = (int)Math.Round(_balance.FuelPickupValue);
            row.Fuel[FieldSpec.CellIndex(lane, layer)] += p.Value;
        }

        private void AddPowerUp(TrackRow row, int lane, int layer, CellType cell, PickupKind kind)
        {
            NewPickup(kind, row, lane, layer, cell, 0.5f);
        }

        private Pickup NewPickup(PickupKind kind, TrackRow row, int lane, int layer, CellType cell, float along)
        {
            float rise = Cells.LayerDelta(cell) * FieldSpec.LayerSpacing * along;
            var p = new Pickup
            {
                Id = _nextPickupId++,
                Kind = kind,
                Lane = lane,
                Layer = layer,
                X = FieldSpec.LaneX(lane),
                Y = FieldSpec.RailY(layer) + rise + (kind == PickupKind.Gem || kind == PickupKind.Fuel ? PickupHeight : PickupHeight + 0.15f),
                Z = FieldSpec.TileStart(row.Tile) + FieldSpec.TileLength * along,
            };
            _rowPickups.Add(p);
            return p;
        }

        private void CommitRowPickups(TrackRow row)
        {
            // Close the gem lines of lanes without gems on this row.
            for (int lane = 0; lane < FieldSpec.LaneCount; lane++)
            {
                bool any = false;
                for (int layer = 0; layer < FieldSpec.LayerCount && !any; layer++)
                {
                    any = row.Gems[FieldSpec.CellIndex(lane, layer)] > 0;
                }

                if (!any)
                {
                    CloseTrail(lane);
                }
            }

            _rowPickups.Sort((a, b) => a.Z.CompareTo(b.Z));
            for (int i = 0; i < _rowPickups.Count; i++)
            {
                _out.Add(_rowPickups[i]);
            }

            _rowPickups.Clear();
        }

        private void CloseTrails(bool all)
        {
            for (int lane = 0; lane < FieldSpec.LaneCount; lane++)
            {
                CloseTrail(lane);
            }
        }

        private void CloseTrail(int lane)
        {
            List<Pickup> trail = _trails[lane];
            if (trail.Count >= MinTrailGems)
            {
                var group = new GemTrail { Id = _nextTrailId++, Size = trail.Count };
                for (int i = 0; i < trail.Count; i++)
                {
                    trail[i].Group = group;
                }
            }

            trail.Clear();
        }

        private PickupKind NextPowerUp()
        {
            float[] w = _balance.PowerUpWeights;
            var weights = new[]
            {
                w[0] * _mods.Drops,
                w[1],
                _spec.Number >= 2 ? w[2] : 0f,
                w[3],
                _spec.Number >= 3 ? w[4] * _mods.Drops : 0f,
            };
            switch (_power.Weighted(weights))
            {
                case 1:
                    return PickupKind.Magnet;
                case 2:
                    return PickupKind.Overload;
                case 3:
                    return PickupKind.InstantBuild;
                case 4:
                    return PickupKind.Repair;
                default:
                    return PickupKind.Shield;
            }
        }

        private double PowerUpGapTiles(int level)
        {
            float seconds = _power.Range(_balance.PowerUpIntervalMin, _balance.PowerUpIntervalMax) / Math.Max(0.2f, _mods.Drops);
            float speed = Levels.LevelStartSpeed(level) * _mods.Speed;
            return seconds * speed / FieldSpec.TileLength;
        }

        // ------------------------------------------------------------------ planning

        /// <summary>Carries the frontier over a committed row.</summary>
        private void Advance(long tile)
        {
            _solver.Step(tile, _frontier, _tmpA, _gridCell, _gridFuel);
            Array.Copy(_tmpA, _frontier, _frontier.Length);
        }

        private CellType GridCell(long tile, int lane, int layer)
        {
            if (tile >= _grid.EndTile)
            {
                return AssumedCell(layer); // not generated yet: plain track on the open layers
            }

            return _grid.Get(tile, lane, layer);
        }

        private float GridFuel(long tile, int lane, int layer)
        {
            TrackRow row = _grid.Row(tile);
            return row != null ? row.Fuel[FieldSpec.CellIndex(lane, layer)] : 0f;
        }

        private CellType ChunkCell(long tile, int lane, int layer)
        {
            long r = tile - _pendingStart;
            if (r >= 0 && r < _pendingCells.Count)
            {
                return _pendingCells[(int)r][FieldSpec.CellIndex(lane, layer)];
            }

            return GridCell(tile, lane, layer);
        }

        private float ChunkFuel(long tile, int lane, int layer)
        {
            long r = tile - _pendingStart;
            if (r >= 0 && r < _pendingFuel.Count)
            {
                return _pendingFuel[(int)r][FieldSpec.CellIndex(lane, layer)];
            }

            return GridFuel(tile, lane, layer);
        }

        private CellType AssumedCell(int layer)
        {
            return layer <= _solver.MaxLayer ? CellType.Track : CellType.Empty;
        }
    }
}

using System;
using System.Collections.Generic;
using BeamRunner.Core.Balance;

namespace BeamRunner.Core.Field
{
    /// <summary>
    /// Lays the track ahead of the craft, tile by tile, into a <see cref="TrackGrid"/> and queues its pickups. Every
    /// level is: <see cref="Levels.IntroTiles"/> of full track (the craft lands on the middle layer), then hand-built
    /// <see cref="Chunk"/>s separated by stretches of plain track on every open layer, then a runway and the closing
    /// wall. A chunk is placed only if the run can afford it: a <see cref="RouteSolver"/> frontier (the most fuel an
    /// optimal pilot can hold on each lane and layer) is carried along, and a chunk that would leave no surviving route
    /// is skipped — low on fuel, fuel chunks are favoured. Pickups and optional obstacles are rolled by the level and
    /// the adaptive modifiers (fuel, obstacles, drops, gems); power-ups keep their own schedule. Deterministic from the
    /// seed; pickups are queued in Z order.
    /// </summary>
    public sealed class TrackGenerator
    {
        /// <summary>Shortest gem line that pays the full-trail bonus.</summary>
        public const int MinTrailGems = 4;

        /// <summary>Below this much (optimal) fuel, fuel chunks are strongly favoured.</summary>
        public const float LowFuel = 6f;

        /// <summary>Height of gems and fuel cells above their rail.</summary>
        public const float PickupHeight = 0.9f;

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
        private readonly List<Chunk> _candidates = new List<Chunk>(64);
        private readonly List<float> _weights = new List<float>(64);
        private readonly List<Pickup> _rowPickups = new List<Pickup>(16);
        private readonly List<Pickup>[] _trails = { new List<Pickup>(), new List<Pickup>(), new List<Pickup>() };
        private readonly RouteSolver.CellReader _gridCell;
        private readonly RouteSolver.FuelReader _gridFuel;
        private readonly RouteSolver.CellReader _chunkCell;
        private readonly RouteSolver.FuelReader _chunkFuel;

        // The chunk being evaluated: its rows as cells and fuel, before they are committed.
        private readonly List<CellType[]> _pendingCells = new List<CellType[]>(16);
        private readonly List<float[]> _pendingFuel = new List<float[]>(16);
        private readonly List<char[]> _pendingCodes = new List<char[]>(16);
        private long _pendingStart;

        private LevelSpec _spec;
        private long _levelStart = long.MinValue;
        private long _wallTile;
        private int _fillerLeft;
        private Chunk _lastChunk;
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

            if (rel < intro || t >= _wallTile - Levels.RunwayTiles)
            {
                WriteFiller(false);
                return;
            }

            if (_fillerLeft > 0)
            {
                _fillerLeft--;
                WriteFiller(true);
                return;
            }

            if (!TryPlaceChunk(t))
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
            _lastChunk = null;
            _solver.MaxLayer = _spec.TopLayer ? FieldSpec.TopLayer : FieldSpec.MiddleLayer;

            // The craft starts every level on the middle layer, in any lane, with whatever fuel it carried over the wall.
            float fuel = first ? _ship.StartFuel : Math.Max(0f, RouteSolver.Best(_frontier));
            RouteSolver.Clear(_frontier);
            for (int lane = 0; lane < FieldSpec.LaneCount; lane++)
            {
                _frontier[RouteSolver.State(lane, FieldSpec.MiddleLayer, BuildKind.None)] = fuel;
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

            CloseTrails(true);
            Advance(row.Tile);
        }

        /// <summary>Plain rail on every open layer; between chunks it may carry fuel, a gem line or a power-up.</summary>
        private void WriteFiller(bool items)
        {
            TrackRow row = _grid.Append(_spec.Number);
            int layers = _spec.TopLayer ? FieldSpec.LayerCount : FieldSpec.LayerCount - 1;
            for (int layer = 0; layer < layers; layer++)
            {
                for (int lane = 0; lane < FieldSpec.LaneCount; lane++)
                {
                    row.Set(lane, layer, CellType.Track);
                }
            }

            _rowPickups.Clear();
            if (items)
            {
                if (_items.Chance(_balance.FillerFuelChance * _spec.FuelScale * _mods.Fuel))
                {
                    int lane = _items.Range(0, FieldSpec.LaneCount);
                    int layer = _items.Range(Math.Min(1, layers - 1), layers);
                    AddFuel(row, lane, layer, CellType.Track);
                }

                if (_items.Chance(0.45f))
                {
                    // A short gem line, richest up high.
                    int lane = _items.Range(0, FieldSpec.LaneCount);
                    int layer = layers - 1;
                    AddGems(row, lane, layer, CellType.Track);
                }

                if (row.Tile >= _nextPowerUpTile)
                {
                    int lane = _items.Range(0, FieldSpec.LaneCount);
                    AddPowerUp(row, lane, layers - 1, CellType.Track, NextPowerUp(), true);
                }
            }

            CommitRowPickups(row);
            Advance(row.Tile);
        }

        // ------------------------------------------------------------------ chunks

        private bool TryPlaceChunk(long t)
        {
            long room = _wallTile - Levels.RunwayTiles - t;
            int maxTier = _tutorial && _spec.Number <= 2 ? 0 : _spec.MaxChunkTier;
            bool low = RouteSolver.Best(_frontier) < LowFuel;
            _candidates.Clear();
            _weights.Clear();
            IReadOnlyList<Chunk> all = ChunkLibrary.All;
            for (int i = 0; i < all.Count; i++)
            {
                Chunk c = all[i];
                if (c.Tier > maxTier || c.Length > room || c == _lastChunk || (c.UsesTop && !_spec.TopLayer) ||
                    (c.HasObstacles && !_spec.Obstacles))
                {
                    continue;
                }

                float w = _spec.TagWeights[(int)c.Tag] * (1f + 0.4f * c.Tier);
                if (c.Tag == ChunkTag.Obstacle)
                {
                    w *= _mods.Obstacles;
                }

                if (c.Tag == ChunkTag.Fuel && low)
                {
                    w = Math.Max(w, 0.5f) * 4f;
                }

                if (w > 0f)
                {
                    _candidates.Add(c);
                    _weights.Add(w);
                }
            }

            for (int attempt = 0; attempt < MaxPicks && _candidates.Count > 0; attempt++)
            {
                int pick = Pick();
                Chunk chunk = _candidates[pick];
                bool mirror = _layout.Chance(0.5f);
                Stage(chunk, mirror, t);
                if (Affordable(t))
                {
                    Commit(chunk);
                    _lastChunk = chunk;
                    float gap = _spec.ChunkGap / Math.Max(0.3f, _mods.Gaps);
                    if (_tutorial && _spec.Number <= 2)
                    {
                        gap *= 2f;
                    }

                    _fillerLeft = Math.Max(1, (int)Math.Round(gap + _layout.Range(0f, 1f)));
                    return true;
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
        }

        /// <summary>True if some route survives the staged chunk from the current frontier.</summary>
        private bool Affordable(long start)
        {
            Array.Copy(_frontier, _tmpA, _tmpA.Length);
            float[] from = _tmpA;
            float[] to = _tmpB;
            for (int r = 0; r < _pendingCells.Count; r++)
            {
                _solver.Step(start + r, from, to, _chunkCell, _chunkFuel);
                float[] swap = from;
                from = to;
                to = swap;
            }

            return RouteSolver.Best(from) >= 0f;
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
                _rowPickups.Clear();
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
                            case '*':
                                AddGems(row, lane, layer, cell);
                                break;
                            case 'f':
                            case 'F':
                                AddFuel(row, lane, layer, cell);
                                break;
                            case 's':
                                AddPowerUp(row, lane, layer, cell, PickupKind.Shield, false);
                                break;
                            case 'r':
                                AddPowerUp(row, lane, layer, cell, PickupKind.Repair, false);
                                break;
                            case 'p':
                                if (row.Tile >= _nextPowerUpTile && powerSlots == 0)
                                {
                                    powerSlots++;
                                    AddPowerUp(row, lane, layer, cell, NextPowerUp(), true);
                                }
                                else
                                {
                                    AddGems(row, lane, layer, cell);
                                }

                                break;
                        }
                    }
                }

                CommitRowPickups(row);
                Advance(row.Tile);
            }
        }

        // ------------------------------------------------------------------ pickups

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

        private void AddPowerUp(TrackRow row, int lane, int layer, CellType cell, PickupKind kind, bool scheduled)
        {
            NewPickup(kind, row, lane, layer, cell, 0.5f);
            if (scheduled)
            {
                _nextPowerUpTile = row.Tile + PowerUpGapTiles(_spec.Number);
            }
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

using System;
using System.Collections.Generic;
using BeamRunner.Core.Balance;
using BeamRunner.Core.Field;
using NUnit.Framework;

namespace BeamRunner.Core.Tests
{
    /// <summary>
    /// The route beats and the generator that strings them into one forced route: every beat starts and ends on the
    /// route rail, can be cleared without a fall on the starting fuel (checked with the <see cref="RouteSolver"/>), and
    /// the generated track keeps the contract the simulation relies on (one wall per level, deterministic output, a route
    /// always alive), is a few rails (the route and its branches, never the open grid) and never puts a pickup over a gap.
    /// </summary>
    public class ChunkTests
    {
        /// <summary>Route rail modelled before and after a beat (the generator joins beats with route rail).</summary>
        private const int PlainTiles = 2;

        [Test]
        public void EveryBeat_FromEveryStart_StartsAndEndsOnTheRoute_WithValidCells()
        {
            int built = 0;
            foreach (BeatKind kind in RouteBeats.All)
            {
                foreach (int maxLayer in new[] { FieldSpec.MiddleLayer, FieldSpec.TopLayer })
                {
                    for (int tier = RouteBeats.TierOf(kind); tier <= 3; tier++)
                    {
                        for (int lane = 0; lane < FieldSpec.LaneCount; lane++)
                        {
                            for (int layer = 0; layer <= maxLayer; layer++)
                            {
                                for (int seed = 1; seed <= 6; seed++)
                                {
                                    Beat beat = RouteBeats.Build(kind, lane, layer, maxLayer, tier, 0.5f, new Rng(seed));
                                    if (beat == null)
                                    {
                                        continue;
                                    }

                                    built++;
                                    string id = kind + " from " + lane + "/" + layer + " max " + maxLayer + " tier " + tier + " seed " + seed;
                                    Chunk c = beat.Chunk;
                                    Assert.That(c.Length, Is.GreaterThanOrEqualTo(3), id);
                                    Assert.AreEqual(CellType.Track, Chunk.CellOf(c.Code(0, lane, layer, false), true), id + ": starts on the route rail");
                                    Assert.AreEqual('=', c.Code(c.Length - 1, beat.EndLane, beat.EndLayer, false), id + ": ends on it");
                                    Assert.That(beat.EndLayer, Is.InRange(0, maxLayer), id);
                                    Assert.AreEqual(RouteBeats.TagOf(kind), c.Tag);
                                    for (int r = 0; r < c.Length; r++)
                                    {
                                        int rails = 0;
                                        for (int l = 0; l < FieldSpec.LaneCount; l++)
                                        {
                                            for (int y = 0; y < FieldSpec.LayerCount; y++)
                                            {
                                                char code = c.Code(r, l, y, false);
                                                Assert.IsTrue("=.^v#gfsrp".IndexOf(code) >= 0, id + ": cell '" + code + "'");
                                                if (y > maxLayer)
                                                {
                                                    Assert.AreEqual('.', code, id + ": above the open layers, row " + r);
                                                }

                                                rails += code == '.' ? 0 : 1;
                                            }
                                        }

                                        Assert.That(rails, Is.LessThanOrEqualTo(2), id + ": at most two rails side by side, row " + r);
                                    }

                                    Assert.AreEqual(kind == BeatKind.Dodge, c.HasObstacles, id + ": only dodges hold cubes");
                                }
                            }
                        }
                    }
                }
            }

            Assert.That(built, Is.GreaterThan(500));
        }

        [Test]
        public void EveryBeat_IsSurvivableWithTheStartingFuel()
        {
            var b = new BalanceConfig();
            ShipStats ship = ShipStats.FromLevels(null, b);
            Assert.AreEqual(b.StartFuel, ship.StartFuel);
            foreach (BeatKind kind in RouteBeats.All)
            {
                for (int lane = 0; lane < FieldSpec.LaneCount; lane++)
                {
                    for (int layer = 0; layer < FieldSpec.LayerCount; layer++)
                    {
                        for (int seed = 1; seed <= 8; seed++)
                        {
                            Beat beat = RouteBeats.Build(kind, lane, layer, FieldSpec.TopLayer, 3, 0f, new Rng(seed));
                            if (beat == null)
                            {
                                continue;
                            }

                            float left = Survive(beat.Chunk, ship, ship.StartFuel, lane, layer, beat.EndLane, beat.EndLayer);
                            Assert.That(left, Is.GreaterThanOrEqualTo(0f),
                                kind + " from " + lane + "/" + layer + " seed " + seed + " cannot be cleared on " + ship.StartFuel + " fuel");
                        }
                    }
                }
            }
        }

        [Test]
        public void ChunkText_Notation()
        {
            var c = new Chunk("test", ChunkTag.Bridge, 0, "2x === =.= ..., ^v# o.f g=s, r=p === ...");
            Assert.AreEqual(4, c.Length, "2x repeats a row");
            Assert.AreEqual('=', c.Code(0, 0, 0, false));
            Assert.AreEqual('.', c.Code(1, 1, 1, false), "groups are bottom, middle, top");
            Assert.AreEqual('^', c.Code(2, 0, 0, false), "lanes left to right");
            Assert.AreEqual('#', c.Code(2, 2, 0, false));
            Assert.AreEqual('g', c.Code(2, 0, FieldSpec.TopLayer, false));
            Assert.AreEqual('#', c.Code(2, 0, 0, true), "mirroring swaps the outer lanes");
            Assert.AreEqual('^', c.Code(2, 2, 0, true));
            Assert.AreEqual('.', c.Code(2, 1, 1, true), "the middle lane stays");
            Assert.AreEqual('p', c.Code(3, 2, 0, false));
            Assert.IsTrue(c.UsesTop);
            Assert.IsTrue(c.HasObstacles);
            Assert.IsFalse(new Chunk("flat", ChunkTag.Bridge, 0, "=== === ..., === .=. ...").UsesTop);
            Assert.AreEqual(CellType.Track, Chunk.CellOf('o', false));
            Assert.AreEqual(CellType.Obstacle, Chunk.CellOf('o', true));
            Assert.AreEqual(CellType.RampDown, Chunk.CellOf('v', false));
            Assert.IsTrue(Chunk.HoldsFuel('f'));
            Assert.IsTrue(Chunk.HoldsGems('g'));
            Assert.Throws<FormatException>(() => new Chunk("gap pickup", ChunkTag.Gems, 0, "=== .*. ..."), "no gems over gaps");
            Assert.Throws<FormatException>(() => new Chunk("gap pickup", ChunkTag.Fuel, 0, "=== .F. ..."), "no fuel over gaps");
            Assert.Throws<FormatException>(() => new Chunk("bad", ChunkTag.Bridge, 0, "=== ==="));
            Assert.Throws<FormatException>(() => new Chunk("bad", ChunkTag.Bridge, 0, "=== ==x ..."));
            Assert.Throws<FormatException>(() => new Chunk("bad", ChunkTag.Bridge, 0, "=== ==== ..."));
        }

        [Test]
        public void ASolidWallOfGaps_IsNotSurvivable_OnTooLittleFuel()
        {
            // The check above can fail. A two-tile gap on every lane and open layer costs two builds; the cheapest pair is
            // a ramp down into the first tile and a bridge on the bottom layer for the second.
            ShipStats ship = ShipStats.FromLevels(null, new BalanceConfig());
            var chasm = new Chunk("chasm", ChunkTag.Bridge, 0, "=== === ..., ... ... ..., ... ... ..., === === ...");
            float cheapest = ship.RampDownCost + ship.BridgeCost;
            Assert.That(cheapest, Is.LessThan(2f * ship.BridgeCost));
            Assert.AreEqual(0f, SurviveAnywhere(chasm, ship, cheapest), 1e-4f);
            Assert.That(SurviveAnywhere(chasm, ship, cheapest - 0.5f), Is.LessThan(0f));
            var ceiling = new Chunk("ceiling", ChunkTag.Edge, 1, "=== === ===, ... ... #=#, === === ===");
            Assert.That(SurviveAnywhere(ceiling, ship, 0f), Is.GreaterThanOrEqualTo(0f), "ride the top-layer middle lane");
            var blocked = new Chunk("blocked", ChunkTag.Obstacle, 2, "=== === ===, ### ### ###, === === ===");
            Assert.That(SurviveAnywhere(blocked, ship, 20f), Is.LessThan(0f), "obstacles cannot be built over");
        }

        /// <summary>
        /// Fuel left on the exit rail (<paramref name="endLane"/>, <paramref name="endLayer"/>) after riding the route
        /// rail into the chunk from (<paramref name="lane"/>, <paramref name="layer"/>) with <paramref name="fuel"/>, the
        /// chunk and the exit rail again (−1 = it can't be reached). Optional obstacles are present (the worst case) and
        /// the chunk's fuel cells are not counted (the level may drop them).
        /// </summary>
        private static float Survive(Chunk chunk, ShipStats ship, float fuel, int lane, int layer, int endLane, int endLayer)
        {
            RouteSolver.CellReader cell = (tile, l, y) =>
            {
                if (tile < 0)
                {
                    return l == lane && y == layer ? CellType.Track : CellType.Empty;
                }

                if (tile >= chunk.Length)
                {
                    return l == endLane && y == endLayer ? CellType.Track : CellType.Empty;
                }

                return Chunk.CellOf(chunk.Code((int)tile, l, y, false), true);
            };
            float[] from = Run(chunk, ship, cell, s => s == RouteSolver.State(lane, layer, BuildKind.None) ? fuel : -1f);
            return from[RouteSolver.State(endLane, endLayer, BuildKind.None)];
        }

        /// <summary>Most fuel left after plain track on every layer, the chunk and plain track again, entering anywhere.</summary>
        private static float SurviveAnywhere(Chunk chunk, ShipStats ship, float fuel)
        {
            RouteSolver.CellReader cell = (tile, lane, layer) => tile < 0 || tile >= chunk.Length
                ? CellType.Track
                : Chunk.CellOf(chunk.Code((int)tile, lane, layer, false), true);
            return RouteSolver.Best(Run(chunk, ship, cell, s => fuel));
        }

        private static float[] Run(Chunk chunk, ShipStats ship, RouteSolver.CellReader cell, Func<int, float> start)
        {
            var solver = new RouteSolver
            {
                BridgeCost = ship.BridgeCost,
                RampUpCost = ship.RampUpCost,
                RampDownCost = ship.RampDownCost,
                MaxFuel = ship.MaxFuel,
                MaxLayer = FieldSpec.TopLayer,
            };
            var from = new float[RouteSolver.StateCount];
            var to = new float[RouteSolver.StateCount];
            for (int s = 0; s < from.Length; s++)
            {
                from[s] = RouteSolver.BuiltOf(s) == BuildKind.None ? start(s) : -1f;
            }

            for (long t = -PlainTiles; t < chunk.Length + PlainTiles; t++)
            {
                solver.Step(t, from, to, cell, null);
                float[] swap = from;
                from = to;
                to = swap;
            }

            return from;
        }

        [Test]
        public void EveryLevelThroughThePlateau_HasBuildBeatsToPickFrom()
        {
            for (int level = 1; level <= Levels.PlateauLevel + Levels.PlateauCycle.Length; level++)
            {
                LevelSpec spec = Levels.Get(level);
                bool any = false;
                foreach (BeatKind k in RouteBeats.All)
                {
                    ChunkTag tag = RouteBeats.TagOf(k);
                    any |= RouteBeats.TierOf(k) <= spec.MaxChunkTier && spec.TagWeights[(int)tag] > 0f &&
                           tag != ChunkTag.Fuel && tag != ChunkTag.Gems && (tag != ChunkTag.Obstacle || spec.Obstacles);
                }

                Assert.IsTrue(any, "level " + level + " has nothing to build or steer through");
            }
        }

        [Test]
        public void GeneratedTrack_IsAFewRails_NotTheOpenGrid_AndNoPickupFloatsOverAGap()
        {
            for (int seed = 1; seed <= 6; seed++)
            {
                var course = new Course(1);
                var grid = new TrackGrid();
                var pickups = new List<Pickup>();
                new TrackGenerator(seed, course, false, null, new BalanceConfig(), null, grid, pickups)
                    .GenerateUntil(course.WallTile(10) + 1);
                int rows = 0;
                int rails = 0;
                for (long t = 0; t <= course.WallTile(10); t++)
                {
                    TrackRow row = grid.Row(t);
                    int level = row.Level;
                    long rel = t - course.LevelStartTile(level);
                    bool open = rel < Levels.IntroTiles + (level == 1 ? 2 : 0) || t >= course.WallTile(level) - Levels.RunwayTiles;
                    if (open)
                    {
                        continue;
                    }

                    int n = 0;
                    for (int i = 0; i < FieldSpec.CellsPerRow; i++)
                    {
                        n += row.Cells[i] == CellType.Empty ? 0 : 1;
                    }

                    Assert.That(n, Is.LessThanOrEqualTo(3), "seed " + seed + " tile " + t + ": more than three rails side by side");
                    rows++;
                    rails += n;
                }

                float mean = rails / (float)rows;
                Assert.That(mean, Is.InRange(1.2f, 2.2f), "seed " + seed + ": the route plus branches, not one rail nor the grid");
                foreach (Pickup p in pickups)
                {
                    long tile = FieldSpec.TileOf(p.Z);
                    Assert.IsTrue(Cells.Solid(grid.Get(tile, p.Lane, p.Layer)) && grid.Get(tile, p.Lane, p.Layer) != CellType.Obstacle,
                        "seed " + seed + ": a " + p.Kind + " over a gap at tile " + tile + " lane " + p.Lane + " layer " + p.Layer);

                    // Never in front of a gap, a drop, a ramp down or a cube: the track goes on behind it in clear sight.
                    int y = p.Layer + Cells.LayerDelta(grid.Get(tile, p.Lane, p.Layer));
                    for (int k = 1; k <= TrackGenerator.SightTiles; k++)
                    {
                        TrackRow row = grid.Row(tile + k);
                        if (row == null || row.IsWall)
                        {
                            break;
                        }

                        CellType c = row.Get(p.Lane, y);
                        Assert.IsTrue(c == CellType.Track || c == CellType.RampUp,
                            "seed " + seed + ": a " + p.Kind + " at tile " + tile + " hides " + c + " " + k + " tile(s) behind it");
                        y += Cells.LayerDelta(c);
                    }
                }

                Assert.That(pickups.Count, Is.GreaterThan(60), "seed " + seed + ": still plenty to pick up");
            }
        }

        [Test]
        public void Generator_IsDeterministic()
        {
            for (int seed = 1; seed <= 4; seed++)
            {
                var gridA = new TrackGrid();
                var gridB = new TrackGrid();
                var a = new List<Pickup>();
                var b = new List<Pickup>();
                new TrackGenerator(seed, new Course(3), false, null, new BalanceConfig(), null, gridA, a).GenerateUntil(400);
                new TrackGenerator(seed, new Course(3), false, null, new BalanceConfig(), null, gridB, b).GenerateUntil(400);
                Assert.AreEqual(gridA.EndTile, gridB.EndTile);
                for (long t = gridA.FirstTile; t < gridA.EndTile; t++)
                {
                    CollectionAssert.AreEqual(gridA.Row(t).Cells, gridB.Row(t).Cells, "seed " + seed + " tile " + t);
                    CollectionAssert.AreEqual(gridA.Row(t).Fuel, gridB.Row(t).Fuel);
                    Assert.AreEqual(gridA.Row(t).Level, gridB.Row(t).Level);
                }

                Assert.AreEqual(a.Count, b.Count);
                for (int i = 0; i < a.Count; i++)
                {
                    Assert.AreEqual(a[i].Kind, b[i].Kind);
                    Assert.AreEqual(a[i].Z, b[i].Z);
                    Assert.AreEqual(a[i].Lane, b[i].Lane);
                    Assert.AreEqual(a[i].Layer, b[i].Layer);
                    Assert.AreEqual(a[i].Value, b[i].Value);
                }

                var other = new TrackGrid();
                new TrackGenerator(seed + 100, new Course(3), false, null, new BalanceConfig(), null, other, new List<Pickup>())
                    .GenerateUntil(400);
                bool differs = false;
                for (long t = 0; t < 400 && !differs; t++)
                {
                    for (int i = 0; i < FieldSpec.CellsPerRow; i++)
                    {
                        differs |= other.Row(t).Cells[i] != gridA.Row(t).Cells[i];
                    }
                }

                Assert.IsTrue(differs, "another seed lays another track");
            }
        }

        [Test]
        public void EveryLevel_EndsInExactlyOneWall_AtItsWallTile()
        {
            foreach (int start in new[] { 1, 4 })
            {
                var course = new Course(start);
                var grid = new TrackGrid();
                int last = start + 7;
                new TrackGenerator(start * 31, course, false, null, new BalanceConfig(), null, grid, new List<Pickup>())
                    .GenerateUntil(course.WallTile(last) + 1);
                var walls = new Dictionary<int, int>();
                for (long t = grid.FirstTile; t <= course.WallTile(last); t++)
                {
                    TrackRow row = grid.Row(t);
                    Assert.AreEqual(t, row.Tile);
                    Assert.AreEqual(course.LevelOfTile(t), row.Level, "tile " + t);
                    bool wall = t == course.WallTile(row.Level);
                    Assert.AreEqual(wall, row.IsWall, "tile " + t + " of level " + row.Level);
                    for (int i = 0; i < FieldSpec.CellsPerRow; i++)
                    {
                        Assert.AreEqual(wall, row.Cells[i] == CellType.Wall, "tile " + t + " cell " + i);
                    }

                    if (wall)
                    {
                        walls[row.Level] = walls.TryGetValue(row.Level, out int n) ? n + 1 : 1;
                    }
                }

                for (int level = start; level <= last; level++)
                {
                    Assert.IsTrue(walls.TryGetValue(level, out int n) && n == 1, "level " + level + " has one wall");
                    Assert.AreEqual(Levels.TilesOf(level), course.WallTile(level) - course.LevelStartTile(level) + 1);
                }
            }
        }

        [Test]
        public void Generator_KeepsTheTopLayerClosedInLevel1_AndObstaclesUntilLevel5()
        {
            bool top = false;
            bool obstacles = false;
            for (int seed = 1; seed <= 4; seed++)
            {
                var course = new Course(1);
                var grid = new TrackGrid();
                var pickups = new List<Pickup>();
                new TrackGenerator(seed, course, false, AdaptiveDifficulty.For(2f), new BalanceConfig(), null, grid, pickups)
                    .GenerateUntil(course.WallTile(6) + 1);
                for (long t = 0; t <= course.WallTile(6); t++)
                {
                    TrackRow row = grid.Row(t);
                    if (row.IsWall)
                    {
                        continue;
                    }

                    for (int lane = 0; lane < FieldSpec.LaneCount; lane++)
                    {
                        for (int layer = 0; layer < FieldSpec.LayerCount; layer++)
                        {
                            CellType c = row.Get(lane, layer);
                            if (row.Level == 1)
                            {
                                Assert.IsFalse(layer == FieldSpec.TopLayer && c != CellType.Empty, "seed " + seed + ": top layer in level 1");
                            }

                            if (row.Level < Levels.ObstacleLevel)
                            {
                                Assert.AreNotEqual(CellType.Obstacle, c, "seed " + seed + ": obstacle in level " + row.Level);
                            }

                            top |= layer == FieldSpec.TopLayer && Cells.Solid(c);
                            obstacles |= c == CellType.Obstacle;
                        }
                    }
                }


                // Level starts: the intro is plain track on the open layers, every lane.
                for (int level = 1; level <= 6; level++)
                {
                    TrackRow first = grid.Row(course.LevelStartTile(level));
                    for (int lane = 0; lane < FieldSpec.LaneCount; lane++)
                    {
                        Assert.AreEqual(CellType.Track, first.Get(lane, FieldSpec.MiddleLayer), "level " + level + " lands on rail");
                    }
                }

                for (int i = 1; i < pickups.Count; i++)
                {
                    Assert.That(pickups[i].Z, Is.GreaterThanOrEqualTo(pickups[i - 1].Z), "pickups queued in Z order");
                }

                foreach (Pickup p in pickups)
                {
                    Assert.AreEqual(FieldSpec.LaneX(p.Lane), p.X, 1e-4f);
                    Assert.AreEqual(p.Lane, FieldSpec.LaneOf(p.X));
                    Assert.That(p.Layer, Is.InRange(0, FieldSpec.TopLayer));
                    Assert.That(p.Value, Is.GreaterThan(0));
                }
            }

            Assert.IsTrue(top, "the top layer opens");
            Assert.IsTrue(obstacles, "obstacles arrive by level 6 at the Expert rating");
        }

        [Test]
        public void Generator_NeverLeavesTheRouteUnreachable_OverTheFirstEightLevels()
        {
            var course = new Course(1);
            long end = course.WallTile(8) + 1;
            foreach (float rating in new[] { -2f, 0f, 2f })
            {
                for (int seed = 1; seed <= 6; seed++)
                {
                    var grid = new TrackGrid();
                    var g = new TrackGenerator(seed, course, false, AdaptiveDifficulty.For(rating), new BalanceConfig(), null, grid,
                        new List<Pickup>());
                    for (long t = 1; t <= end; t++)
                    {
                        g.GenerateUntil(t);
                        Assert.That(g.FrontierBest, Is.GreaterThanOrEqualTo(0f),
                            "seed " + seed + " rating " + rating + ": no route left at tile " + grid.EndTile + " (level " +
                            course.LevelOfTile(grid.EndTile - 1) + ")");
                    }
                }
            }
        }
    }
}

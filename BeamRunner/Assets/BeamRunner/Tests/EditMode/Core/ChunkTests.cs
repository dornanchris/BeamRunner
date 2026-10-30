using System;
using System.Collections.Generic;
using BeamRunner.Core.Balance;
using BeamRunner.Core.Field;
using NUnit.Framework;

namespace BeamRunner.Core.Tests
{
    /// <summary>
    /// The chunk library and the generator that stitches it: every chunk parses and can be cleared without a fall on the
    /// starting fuel (checked with the <see cref="RouteSolver"/>, mirrored and not), and the generated track keeps the
    /// contract the simulation relies on (one wall per level, deterministic output, a route always alive).
    /// </summary>
    public class ChunkTests
    {
        /// <summary>Plain track modelled before and after a chunk (the generator puts filler between chunks).</summary>
        private const int PlainTiles = 2;

        [Test]
        public void EveryChunk_Parses_WithUniqueId_AndAtLeastThreeRows()
        {
            IReadOnlyList<Chunk> all = null;
            Assert.DoesNotThrow(() => all = ChunkLibrary.All, "every chunk string parses");
            Assert.That(all.Count, Is.GreaterThan(0));
            var ids = new HashSet<string>();
            foreach (Chunk chunk in all)
            {
                Assert.IsFalse(string.IsNullOrEmpty(chunk.Id));
                Assert.IsTrue(ids.Add(chunk.Id), "duplicate chunk id " + chunk.Id);
                Assert.That(chunk.Length, Is.GreaterThanOrEqualTo(3), chunk.Id);
                Assert.That(chunk.Tier, Is.InRange(0, 3), chunk.Id);
                Assert.That((int)chunk.Tag, Is.InRange(0, Chunk.TagCount - 1), chunk.Id);
                foreach (char[] row in chunk.Rows)
                {
                    Assert.AreEqual(FieldSpec.CellsPerRow, row.Length, chunk.Id);
                    foreach (char c in row)
                    {
                        Assert.IsTrue("=.^v#ogf*Fsrp".IndexOf(c) >= 0, chunk.Id + ": unknown cell '" + c + "'");
                    }
                }
            }
        }

        [Test]
        public void ChunkText_Notation()
        {
            var c = new Chunk("test", ChunkTag.Bridge, 0, "2x === =.= ..., ^v# o*f F=s, r=p === ...");
            Assert.AreEqual(4, c.Length, "2x repeats a row");
            Assert.AreEqual('=', c.Code(0, 0, 0, false));
            Assert.AreEqual('.', c.Code(1, 1, 1, false), "groups are bottom, middle, top");
            Assert.AreEqual('^', c.Code(2, 0, 0, false), "lanes left to right");
            Assert.AreEqual('#', c.Code(2, 2, 0, false));
            Assert.AreEqual('F', c.Code(2, 0, FieldSpec.TopLayer, false));
            Assert.AreEqual('#', c.Code(2, 0, 0, true), "mirroring swaps the outer lanes");
            Assert.AreEqual('^', c.Code(2, 2, 0, true));
            Assert.AreEqual('*', c.Code(2, 1, 1, true), "the middle lane stays");
            Assert.AreEqual('p', c.Code(3, 2, 0, false));
            Assert.IsTrue(c.UsesTop);
            Assert.IsTrue(c.HasObstacles);
            Assert.IsFalse(new Chunk("flat", ChunkTag.Bridge, 0, "=== === ..., === .=. ...").UsesTop);
            Assert.AreEqual(CellType.Track, Chunk.CellOf('o', false));
            Assert.AreEqual(CellType.Obstacle, Chunk.CellOf('o', true));
            Assert.AreEqual(CellType.Empty, Chunk.CellOf('*', true), "gems over a gap");
            Assert.AreEqual(CellType.Empty, Chunk.CellOf('F', true), "fuel over a gap");
            Assert.AreEqual(CellType.RampDown, Chunk.CellOf('v', false));
            Assert.IsTrue(Chunk.HoldsFuel('F'));
            Assert.IsTrue(Chunk.HoldsGems('g'));
            Assert.Throws<FormatException>(() => new Chunk("bad", ChunkTag.Bridge, 0, "=== ==="));
            Assert.Throws<FormatException>(() => new Chunk("bad", ChunkTag.Bridge, 0, "=== ==x ..."));
            Assert.Throws<FormatException>(() => new Chunk("bad", ChunkTag.Bridge, 0, "=== ==== ..."));
        }

        [Test]
        public void TierZeroChunks_NeverUseTheTopLayer_OrObstacles()
        {
            foreach (Chunk chunk in ChunkLibrary.All)
            {
                if (chunk.Tier == 0)
                {
                    Assert.IsFalse(chunk.UsesTop, chunk.Id + " is placed in level 1, where the top layer is closed");
                    for (int r = 0; r < chunk.Length; r++)
                    {
                        for (int lane = 0; lane < FieldSpec.LaneCount; lane++)
                        {
                            Assert.AreEqual('.', chunk.Code(r, lane, FieldSpec.TopLayer, false), chunk.Id + " row " + r);
                        }
                    }
                }

                if (chunk.Tier < 2)
                {
                    Assert.IsFalse(chunk.HasObstacles, chunk.Id + ": obstacles are tier 2+ (level 5+)");
                }
            }
        }

        [Test]
        public void EveryChunk_IsSurvivableWithTheStartingFuel_MirroredAndNot()
        {
            var b = new BalanceConfig();
            ShipStats ship = ShipStats.FromLevels(null, b);
            Assert.AreEqual(b.StartFuel, ship.StartFuel);
            foreach (Chunk chunk in ChunkLibrary.All)
            {
                foreach (bool mirror in new[] { false, true })
                {
                    float left = Survive(chunk, mirror, ship, ship.StartFuel);
                    Assert.That(left, Is.GreaterThanOrEqualTo(0f),
                        chunk.Id + (mirror ? " (mirrored)" : "") + " cannot be cleared without a fall on " + ship.StartFuel + " fuel");
                }
            }
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
            Assert.AreEqual(0f, Survive(chasm, false, ship, cheapest), 1e-4f);
            Assert.That(Survive(chasm, false, ship, cheapest - 0.5f), Is.LessThan(0f));
            var ceiling = new Chunk("ceiling", ChunkTag.Edge, 1, "=== === ===, ... ... #=#, === === ===");
            Assert.That(Survive(ceiling, false, ship, 0f), Is.GreaterThanOrEqualTo(0f), "ride the top-layer middle lane");
            var blocked = new Chunk("blocked", ChunkTag.Obstacle, 2, "=== === ===, ### ### ###, === === ===");
            Assert.That(Survive(blocked, false, ship, 20f), Is.LessThan(0f), "obstacles cannot be built over");
        }

        /// <summary>
        /// Most fuel left after riding plain track, the chunk and plain track again (−1 = no surviving route), entering on
        /// any lane and any layer its tier may use (tier 0 = level 1: the bottom and middle layers) with
        /// <paramref name="fuel"/>. Optional obstacles are present (the worst case) and the chunk's fuel cells are not
        /// counted (the level may drop them).
        /// </summary>
        private static float Survive(Chunk chunk, bool mirror, ShipStats ship, float fuel)
        {
            int maxLayer = chunk.Tier == 0 ? FieldSpec.MiddleLayer : FieldSpec.TopLayer;
            var solver = new RouteSolver
            {
                BridgeCost = ship.BridgeCost,
                RampUpCost = ship.RampUpCost,
                RampDownCost = ship.RampDownCost,
                MaxFuel = ship.MaxFuel,
                MaxLayer = maxLayer,
            };
            RouteSolver.CellReader cell = (tile, lane, layer) =>
            {
                if (tile < 0 || tile >= chunk.Length)
                {
                    return layer <= maxLayer ? CellType.Track : CellType.Empty;
                }

                return Chunk.CellOf(chunk.Code((int)tile, lane, layer, mirror), true);
            };
            var from = new float[RouteSolver.StateCount];
            var to = new float[RouteSolver.StateCount];
            RouteSolver.Clear(from);
            for (int lane = 0; lane < FieldSpec.LaneCount; lane++)
            {
                for (int layer = 0; layer <= maxLayer; layer++)
                {
                    from[RouteSolver.State(lane, layer, BuildKind.None)] = fuel;
                }
            }

            for (long t = -PlainTiles; t < chunk.Length + PlainTiles; t++)
            {
                solver.Step(t, from, to, cell, null);
                float[] swap = from;
                from = to;
                to = swap;
            }

            return RouteSolver.Best(from);
        }

        [Test]
        public void EveryLevelThroughThePlateau_HasChunksToPickFrom()
        {
            for (int level = 1; level <= Levels.PlateauLevel + Levels.PlateauCycle.Length; level++)
            {
                LevelSpec spec = Levels.Get(level);
                bool any = false;
                foreach (Chunk c in ChunkLibrary.All)
                {
                    any |= c.Tier <= spec.MaxChunkTier && spec.TagWeights[(int)c.Tag] > 0f && (!c.UsesTop || spec.TopLayer) &&
                           (!c.HasObstacles || spec.Obstacles);
                }

                Assert.IsTrue(any, "level " + level + " has no chunk");
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

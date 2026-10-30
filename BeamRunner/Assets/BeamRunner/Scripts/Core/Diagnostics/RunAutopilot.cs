using System;
using BeamRunner.Core.Field;
using BeamRunner.Core.Sim;

namespace BeamRunner.Core.Diagnostics
{
    /// <summary>
    /// Bots that ride a <see cref="RunSimulation"/> by its public state only (the grid it exposes, no generator internals):
    /// the menu's attract run, the footage pilot, the PlayMode smoke tests and the balance tests. Pure and deterministic.
    /// <see cref="FollowLine"/> plans a few tiles ahead with a <see cref="RouteSolver"/>: once per tile it picks the lane
    /// and the build for the next tile that keep a route alive with the most fuel, with a pull toward gems and fuel and a
    /// preference for building nothing; in a fall it steers toward a rail to land on. <see cref="SeekCrash"/> builds ramps
    /// down until it drops into the void.
    /// </summary>
    public static class RunAutopilot
    {
        /// <summary>Tiles the planner looks past the next one.</summary>
        public const int HorizonTiles = 6;

        /// <summary>How far into a tile (0..1) the bot waits before deciding (the lane switch must fit in the tile).</summary>
        private const float DecideAt = 0.12f;

        private static readonly object Lock = new object();
        private static readonly RouteSolver Solver = new RouteSolver();
        private static readonly float[] BufA = new float[RouteSolver.StateCount];
        private static readonly float[] BufB = new float[RouteSolver.StateCount];
        private static RunSimulation _sim;
        private static readonly RouteSolver.CellReader Cell = ReadCell;
        private static readonly RouteSolver.FuelReader FuelAt = ReadFuel;

        public static RunInput Sample(RunSimulation sim, AutopilotMode mode)
        {
            switch (mode)
            {
                case AutopilotMode.FollowLine:
                    return FollowLine(sim);
                case AutopilotMode.SeekCrash:
                    return SeekCrash(sim);
                default:
                    return new RunInput();
            }
        }

        /// <summary>Plays the run: picks a lane and builds what the route ahead needs.</summary>
        public static RunInput FollowLine(RunSimulation sim)
        {
            if (sim == null || sim.IsCrashed)
            {
                return new RunInput();
            }

            lock (Lock)
            {
                _sim = sim;
                try
                {
                    if (sim.Phase == RunPhase.Falling)
                    {
                        return new RunInput { UseAbsoluteLane = true, AbsoluteLane = LandingLane(sim) };
                    }

                    if (sim.Phase != RunPhase.Riding)
                    {
                        return new RunInput();
                    }

                    double along = (sim.Distance - FieldSpec.TileStart(sim.Tile)) / FieldSpec.TileLength;
                    if (along < DecideAt)
                    {
                        return new RunInput();
                    }

                    int lane;
                    BuildKind build;
                    Plan(sim, out lane, out build);
                    return new RunInput { UseAbsoluteLane = true, AbsoluteLane = lane, Build = build };
                }
                finally
                {
                    _sim = null;
                }
            }
        }

        /// <summary>Builds ramps down until the craft drops into the void (for crash tests).</summary>
        public static RunInput SeekCrash(RunSimulation sim)
        {
            if (sim == null || sim.Phase != RunPhase.Riding)
            {
                return new RunInput();
            }

            long tile;
            int lane;
            int layer;
            if (!sim.BuildTarget(out tile, out lane, out layer) || sim.Grid.Get(tile, lane, layer) == CellType.RampDown)
            {
                return new RunInput();
            }

            return new RunInput { Build = BuildKind.RampDown };
        }

        // ------------------------------------------------------------------ planning

        private static void Plan(RunSimulation sim, out int bestLane, out BuildKind bestBuild)
        {
            ShipStatsCosts(sim);
            long t = sim.Tile;
            int lane = sim.TargetLane;
            int layer = sim.Layer;
            CellType here = sim.Grid.Get(t, lane, layer);
            bool flat = here == CellType.Track || here == CellType.Wall;
            int exitLayer = layer + (Cells.IsRamp(here) ? Cells.LayerDelta(here) : 0);
            if (exitLayer < 0 || exitLayer > FieldSpec.TopLayer)
            {
                exitLayer = Math.Max(0, Math.Min(FieldSpec.TopLayer, exitLayer));
            }

            bestLane = lane;
            bestBuild = BuildKind.None;
            float bestScore = float.MinValue;
            for (int d = -1; d <= 1; d++)
            {
                int l2 = lane + d;
                if (l2 < 0 || l2 >= FieldSpec.LaneCount)
                {
                    continue;
                }

                if (d != 0 && (!flat || sim.Grid.Get(t, l2, layer) != CellType.Track))
                {
                    continue;
                }

                CellType next = sim.Grid.Get(t + 1, l2, exitLayer);
                TrackRow nextRow = sim.Grid.Row(t + 1);
                for (int b = 0; b < 4; b++)
                {
                    var kind = (BuildKind)b;
                    if (kind != BuildKind.None)
                    {
                        if (!sim.CanAfford(kind) || next == CellType.Obstacle || next == CellType.Wall ||
                            nextRow != null && nextRow.IsWall || next == Cells.FromBuild(kind))
                        {
                            continue;
                        }

                        if (kind == BuildKind.RampUp && exitLayer >= FieldSpec.TopLayer ||
                            kind == BuildKind.RampDown && exitLayer <= 0)
                        {
                            continue; // the ceiling and the void
                        }
                    }

                    float fuel = sim.Fuel - sim.BuildCost(kind);
                    float score = Evaluate(sim, t + 1, l2, exitLayer, kind, fuel);
                    if (score <= float.MinValue)
                    {
                        continue;
                    }

                    score -= Math.Abs(d) * 0.15f + sim.BuildCost(kind) * 0.35f;
                    if (nextRow != null)
                    {
                        int cell = FieldSpec.CellIndex(l2, exitLayer);
                        score += nextRow.Gems[cell] * 0.6f + nextRow.Fuel[cell] * 0.3f;
                    }

                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestLane = l2;
                        bestBuild = kind;
                    }
                }
            }

            if (bestScore <= float.MinValue)
            {
                // Nothing survives the horizon: at least keep a rail under the craft.
                bestLane = lane;
                bestBuild = sim.Grid.Get(t + 1, lane, exitLayer) == CellType.Empty && sim.CanAfford(BuildKind.Bridge)
                    ? BuildKind.Bridge
                    : BuildKind.None;
            }
        }

        /// <summary>Best fuel left after the horizon when entering <paramref name="tile"/> as given, or MinValue.</summary>
        private static float Evaluate(RunSimulation sim, long tile, int lane, int layer, BuildKind built, float fuel)
        {
            if (fuel < -1e-4f)
            {
                return float.MinValue;
            }

            RouteSolver.Clear(BufA);
            BufA[RouteSolver.State(lane, layer, built)] = Math.Max(0f, fuel);
            float[] from = BufA;
            float[] to = BufB;
            for (int i = 0; i < HorizonTiles; i++)
            {
                TrackRow row = sim.Grid.Row(tile + i);
                if (row != null && row.IsWall)
                {
                    return RouteSolver.Best(from) + 2f; // reaching the wall is the goal
                }

                Solver.Step(tile + i, from, to, Cell, FuelAt);
                float[] swap = from;
                from = to;
                to = swap;
                if (RouteSolver.Best(from) < 0f)
                {
                    return float.MinValue;
                }
            }

            return RouteSolver.Best(from);
        }

        private static void ShipStatsCosts(RunSimulation sim)
        {
            Solver.BridgeCost = sim.BuildCost(BuildKind.Bridge);
            Solver.RampUpCost = sim.BuildCost(BuildKind.RampUp);
            Solver.RampDownCost = sim.BuildCost(BuildKind.RampDown);
            Solver.MaxFuel = sim.MaxFuel;
            Solver.MaxLayer = FieldSpec.TopLayer;
        }

        /// <summary>While falling: the nearest lane with a rail on the layer below (this tile or the next).</summary>
        private static int LandingLane(RunSimulation sim)
        {
            int below = sim.Layer - 1;
            int best = sim.TargetLane;
            int bestCost = int.MaxValue;
            for (int lane = 0; lane < FieldSpec.LaneCount; lane++)
            {
                bool rail = below >= 0 && (Cells.Solid(sim.Grid.Get(sim.Tile, lane, below)) &&
                                           sim.Grid.Get(sim.Tile, lane, below) != CellType.Obstacle);
                int cost = Math.Abs(lane - sim.Lane) + (rail ? 0 : 10);
                if (cost < bestCost)
                {
                    bestCost = cost;
                    best = lane;
                }
            }

            return best;
        }

        private static CellType ReadCell(long tile, int lane, int layer)
        {
            RunSimulation sim = _sim;
            if (sim == null)
            {
                return CellType.Empty;
            }

            TrackRow row = sim.Grid.Row(tile);
            if (row == null)
            {
                return layer <= FieldSpec.MiddleLayer ? CellType.Track : CellType.Empty;
            }

            return row.Get(lane, layer);
        }

        private static float ReadFuel(long tile, int lane, int layer)
        {
            RunSimulation sim = _sim;
            TrackRow row = sim != null ? sim.Grid.Row(tile) : null;
            return row != null ? row.Fuel[FieldSpec.CellIndex(lane, layer)] : 0f;
        }
    }
}

using System;
using BeamRunner.Core.Field;
using BeamRunner.Core.Sim;

namespace BeamRunner.Core.Diagnostics
{
    /// <summary>
    /// Bots that ride a <see cref="RunSimulation"/> by its public state only (the grid it exposes, no generator internals):
    /// the menu's attract run, the footage pilot, the PlayMode smoke tests and the balance tests. Pure and deterministic.
    /// <see cref="FollowLine"/> plans a few tiles ahead with a <see cref="RouteSolver"/>: each tile it picks the build for
    /// the next tile — nothing, a bridge, a ramp or a diagonal into the next lane — that keeps a route alive with the most
    /// fuel, with a pull toward gems and fuel and a preference for building nothing; in a fall it steers toward a rail to
    /// land on. <see cref="SeekCrash"/> builds ramps
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

                    return new RunInput { Build = Plan(sim) };
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

        /// <summary>
        /// The build for the next tile (None = ride what is there; a switch = the diagonal a swipe builds) that keeps a
        /// route alive with the most fuel, with a pull toward gems and fuel and a slight preference for building nothing.
        /// </summary>
        private static BuildKind Plan(RunSimulation sim)
        {
            ShipStatsCosts(sim);
            long tile;
            int lane;
            int layer;
            if (!sim.BuildTarget(out tile, out lane, out layer))
            {
                return BuildKind.None;
            }

            CellType next = sim.Grid.Get(tile, lane, layer);
            TrackRow nextRow = sim.Grid.Row(tile);
            BuildKind best = BuildKind.None;
            float bestScore = float.MinValue;
            for (int b = 0; b < 6; b++)
            {
                var kind = (BuildKind)b;
                int toLane = lane + (kind == BuildKind.SwitchLeft ? -1 : kind == BuildKind.SwitchRight ? 1 : 0);
                float cost = Cells.IsSwitch(kind) ? sim.SwitchCost(next) : sim.BuildCost(kind);
                if (kind != BuildKind.None)
                {
                    if (cost > sim.Fuel + 1e-4f || next == CellType.Obstacle || next == CellType.Wall ||
                        nextRow != null && nextRow.IsWall || next == Cells.FromBuild(kind))
                    {
                        continue;
                    }

                    if (kind == BuildKind.RampUp && layer >= FieldSpec.TopLayer || kind == BuildKind.RampDown && layer <= 0 ||
                        toLane < 0 || toLane >= FieldSpec.LaneCount || sim.Grid.Get(tile, toLane, layer) == CellType.Obstacle)
                    {
                        continue; // the ceiling, the void, the field's side, a cube beside the diagonal
                    }
                }

                float fuel = sim.Fuel - cost;
                float score = Evaluate(sim, tile, lane, layer, kind, fuel);
                if (score <= float.MinValue)
                {
                    continue;
                }

                score -= (Cells.IsSwitch(kind) ? 0.15f : 0f) + cost * 0.35f;
                TrackRow after = sim.Grid.Row(tile + 1);
                if (after != null)
                {
                    int cell = FieldSpec.CellIndex(toLane, layer);
                    score += after.Gems[cell] * 0.6f + after.Fuel[cell] * 0.3f;
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    best = kind;
                }
            }

            if (bestScore <= float.MinValue && next == CellType.Empty && sim.CanAfford(BuildKind.Bridge))
            {
                return BuildKind.Bridge; // nothing survives the horizon: at least keep a rail under the craft
            }

            return best;
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

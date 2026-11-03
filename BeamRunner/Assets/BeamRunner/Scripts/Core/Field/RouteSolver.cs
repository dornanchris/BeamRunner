using System;

namespace BeamRunner.Core.Field
{
    /// <summary>
    /// Plans routes through the rail grid: "with this much fuel, which (lane, layer) can a pilot still be on at tile t, and
    /// with how much fuel left at best?" A dynamic program over tiles whose state is the craft's lane and layer as it
    /// enters a tile plus what it built on that tile; the value is the most fuel it can have there (−1 = unreachable).
    /// Moves follow the game's rules: every move is a build on the next tile — a bridge, a ramp up or down (fuel) or a
    /// diagonal into the next lane (a switch: free over rail, a bridge's price over a gap) — or riding what is there; gaps, obstacles, the ceiling and the void
    /// end the route (a fair route never needs a fall). Used by the generator (only affordable chunks are placed), the autopilot and the tests.
    /// </summary>
    public sealed class RouteSolver
    {
        /// <summary>States per tile: lane × layer × what was built on that cell (<see cref="BuildKind"/>).</summary>
        public const int StateCount = FieldSpec.CellsPerRow * BuildKinds;

        public const float Unreachable = -1f;

        private const int BuildKinds = 6;

        /// <summary>Reads the generated cell at (tile, lane, layer).</summary>
        public delegate CellType CellReader(long tile, int lane, int layer);

        /// <summary>Fuel a pilot riding (tile, lane, layer) collects.</summary>
        public delegate float FuelReader(long tile, int lane, int layer);

        public float BridgeCost = 2f;
        public float RampUpCost = 3f;
        public float RampDownCost = 1f;
        public float MaxFuel = 20f;

        /// <summary>Highest layer routes may use (1 while the top layer is closed).</summary>
        public int MaxLayer = FieldSpec.TopLayer;

        public static int State(int lane, int layer, BuildKind built)
        {
            return FieldSpec.CellIndex(lane, layer) * BuildKinds + (int)built;
        }

        public static int LaneOf(int state)
        {
            return state / BuildKinds % FieldSpec.LaneCount;
        }

        public static int LayerOf(int state)
        {
            return state / BuildKinds / FieldSpec.LaneCount;
        }

        public static BuildKind BuiltOf(int state)
        {
            return (BuildKind)(state % BuildKinds);
        }

        /// <summary>Fuel a build costs.</summary>
        public float Cost(BuildKind kind)
        {
            switch (kind)
            {
                case BuildKind.Bridge:
                    return BridgeCost;
                case BuildKind.RampUp:
                    return RampUpCost;
                case BuildKind.RampDown:
                    return RampDownCost;
                default:
                    return 0f;
            }
        }

        /// <summary>Fills <paramref name="values"/> with <see cref="Unreachable"/>.</summary>
        public static void Clear(float[] values)
        {
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = Unreachable;
            }
        }

        /// <summary>Best fuel over every reachable state (−1 when none is).</summary>
        public static float Best(float[] values)
        {
            float best = Unreachable;
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] > best)
                {
                    best = values[i];
                }
            }

            return best;
        }

        /// <summary>
        /// One tile of the program: from the states entering <paramref name="tile"/> (<paramref name="from"/>) to the
        /// states entering the next tile (<paramref name="to"/>, overwritten).
        /// </summary>
        public void Step(long tile, float[] from, float[] to, CellReader cell, FuelReader fuel)
        {
            Clear(to);
            for (int s = 0; s < StateCount; s++)
            {
                float f = from[s];
                if (f < 0f)
                {
                    continue;
                }

                int lane = LaneOf(s);
                int layer = LayerOf(s);
                BuildKind built = BuiltOf(s);
                int exitLane;
                int exitLayer;
                if (!Ride(tile, lane, layer, built, cell, out exitLane, out exitLayer))
                {
                    continue;
                }

                f = Math.Min(MaxFuel, f + (fuel != null ? fuel(tile, lane, layer) : 0f));
                CellType next = cell(tile + 1, exitLane, exitLayer);
                for (int b = 0; b < BuildKinds; b++)
                {
                    var kind = (BuildKind)b;
                    float cost = Cells.IsSwitch(kind) ? (next == CellType.Empty ? BridgeCost : 0f) : Cost(kind);
                    if (cost > f + 1e-4f)
                    {
                        continue;
                    }

                    if (kind != BuildKind.None && (next == CellType.Obstacle || next == CellType.Wall))
                    {
                        continue; // building into an obstacle crashes; the wall can't be built on
                    }

                    int toLane = exitLane + (kind == BuildKind.SwitchLeft ? -1 : kind == BuildKind.SwitchRight ? 1 : 0);
                    if (toLane < 0 || toLane >= FieldSpec.LaneCount ||
                        toLane != exitLane && cell(tile + 1, toLane, exitLayer) == CellType.Obstacle)
                    {
                        continue; // a diagonal runs into the next lane over its tile: never through a cube there
                    }

                    int t = State(exitLane, exitLayer, kind);
                    float left = f - cost;
                    if (left > to[t])
                    {
                        to[t] = left;
                    }
                }
            }
        }

        /// <summary>
        /// Rides one cell: false if it ends the route (a gap, an obstacle, the ceiling, the void or a closed layer);
        /// otherwise the lane and layer the craft leaves it on.
        /// </summary>
        public bool Ride(long tile, int lane, int layer, BuildKind built, CellReader cell, out int exitLane, out int exitLayer)
        {
            CellType c = cell(tile, lane, layer);
            if (built != BuildKind.None && c != CellType.Wall)
            {
                // A build replaces the cell, but one into an obstacle crashes (decided before the cell was known).
                c = c == CellType.Obstacle ? CellType.Empty : Cells.FromBuild(built);
            }

            exitLane = lane;
            exitLayer = layer;
            if (c == CellType.Empty || c == CellType.Obstacle)
            {
                return false;
            }

            if (c == CellType.Wall)
            {
                return true;
            }

            exitLane = lane + Cells.LaneDelta(c);
            exitLayer = layer + Cells.LayerDelta(c);
            return exitLayer >= 0 && exitLayer <= MaxLayer && exitLane >= 0 && exitLane < FieldSpec.LaneCount;
        }
    }
}

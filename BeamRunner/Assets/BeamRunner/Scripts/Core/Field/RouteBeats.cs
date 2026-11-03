using System.Collections.Generic;

namespace BeamRunner.Core.Field
{
    /// <summary>The kinds of beat the route is built from (see <see cref="RouteBeats"/>).</summary>
    public enum BeatKind
    {
        /// <summary>A gap in the route: bridge it (in early levels a net one layer down catches a miss).</summary>
        Gap = 0,

        /// <summary>The rail moves to a neighbouring lane: the two overlap briefly, switch while they do.</summary>
        Shift,

        /// <summary>Gems or fuel on the route: a breather.</summary>
        Fuel,

        /// <summary>A gem line along the route.</summary>
        Gems,

        /// <summary>Shift, then a gap straight after it in the new lane.</summary>
        ShiftGap,

        /// <summary>The rail ends and goes on a layer up: build a ramp up.</summary>
        RampUp,

        /// <summary>The rail ends and goes on a layer down: build a ramp down, or fall (a hull point).</summary>
        Drop,

        /// <summary>A ramp to ride (free) up or down a layer.</summary>
        RideRamp,

        /// <summary>A second rail beside the route for a few tiles: plain one way, a gap and gems (or fuel) the other.</summary>
        Fork,

        /// <summary>Up to the top layer under the ceiling, a gappy gem run, and back down.</summary>
        SkyRun,

        /// <summary>Down to the bottom layer over the void, gaps with nothing below, and back up.</summary>
        VoidWalk,

        /// <summary>Two shifts in a row.</summary>
        Weave,

        /// <summary>A cube on the route: dodge into the lane beside it and back (or on).</summary>
        Dodge,

        /// <summary>Two or three of the build/shift beats back to back.</summary>
        Combo,
    }

    /// <summary>A beat built for a route position: its rows as a <see cref="Chunk"/>, and where the route leaves it.</summary>
    public sealed class Beat
    {
        public BeatKind Kind;
        public Chunk Chunk;
        public int EndLane;
        public int EndLayer;
    }

    /// <summary>
    /// The beats a level's single route is made of. The route is one rail — a lane on a layer — and every beat starts on
    /// it and leaves it on a (possibly different) lane and layer, so the pilot is always steered somewhere: switch lanes
    /// while two rails overlap, bridge gaps, build ramps up and down to where the rail goes on, dodge cubes. Beside the
    /// route there is at most a second rail (an overlap, a fork, a dodge lane, an early-level net), never the old open
    /// 3 × 3 grid. Pickups ride only on rails, never over a gap (a gem in the air reads as track that isn't there).
    /// Every beat starts and ends with a plain tile of route rail. Built as <see cref="Chunk"/> rows (never mirrored), so
    /// the generator checks and commits them like the chunks it used to stitch.
    /// </summary>
    public static class RouteBeats
    {
        /// <summary>Every kind, in tier order.</summary>
        public static readonly BeatKind[] All =
        {
            BeatKind.Gap, BeatKind.Shift, BeatKind.Fuel, BeatKind.Gems, BeatKind.ShiftGap, BeatKind.RampUp, BeatKind.Drop,
            BeatKind.RideRamp, BeatKind.Fork, BeatKind.SkyRun, BeatKind.VoidWalk, BeatKind.Weave, BeatKind.Dodge,
            BeatKind.Combo,
        };

        private static readonly BeatKind[] ComboParts =
        {
            BeatKind.Gap, BeatKind.Shift, BeatKind.ShiftGap, BeatKind.RampUp, BeatKind.Drop,
        };

        /// <summary>The tag a level weighs the kind by.</summary>
        public static ChunkTag TagOf(BeatKind kind)
        {
            switch (kind)
            {
                case BeatKind.RampUp:
                case BeatKind.Drop:
                case BeatKind.RideRamp:
                    return ChunkTag.Ramp;
                case BeatKind.SkyRun:
                case BeatKind.VoidWalk:
                    return ChunkTag.Edge;
                case BeatKind.Dodge:
                    return ChunkTag.Obstacle;
                case BeatKind.Gems:
                case BeatKind.Fork:
                    return ChunkTag.Gems;
                case BeatKind.Fuel:
                    return ChunkTag.Fuel;
                default:
                    return ChunkTag.Bridge;
            }
        }

        /// <summary>Lowest chunk tier (<see cref="LevelSpec.MaxChunkTier"/>) that may use the kind.</summary>
        public static int TierOf(BeatKind kind)
        {
            switch (kind)
            {
                case BeatKind.Gap:
                case BeatKind.Shift:
                case BeatKind.Fuel:
                case BeatKind.Gems:
                    return 0;
                case BeatKind.Weave:
                case BeatKind.Dodge:
                    return 2;
                case BeatKind.Combo:
                    return 3;
                default:
                    return 1;
            }
        }

        /// <summary>
        /// Builds a beat of <paramref name="kind"/> starting on the route at (<paramref name="lane"/>,
        /// <paramref name="layer"/>), within layers 0..<paramref name="maxLayer"/>; null if it can't start there (no
        /// layer above or below, …). <paramref name="tier"/> is the level's chunk tier cap (longer gaps, tighter
        /// overlaps as it rises); <paramref name="netChance"/> the chance a gap gets a catch net one layer down.
        /// </summary>
        public static Beat Build(BeatKind kind, int lane, int layer, int maxLayer, int tier, float netChance, Rng rng)
        {
            var b = new Builder(lane, layer);
            if (!Write(b, kind, maxLayer, tier, netChance, rng))
            {
                return null;
            }

            b.Rail(); // every beat leaves on a plain tile of the route
            return new Beat
            {
                Kind = kind,
                Chunk = new Chunk("beat_" + kind, TagOf(kind), TierOf(kind), b.Rows.ToArray()),
                EndLane = b.Lane,
                EndLayer = b.Layer,
            };
        }

        private static bool Write(Builder b, BeatKind kind, int maxLayer, int tier, float netChance, Rng rng)
        {
            switch (kind)
            {
                case BeatKind.Gap:
                {
                    int n = 1 + rng.Range(0, tier >= 3 ? 3 : tier >= 1 ? 2 : 1);
                    bool net = b.Layer > 0 && rng.Chance(netChance);
                    b.Rail();
                    for (int k = 0; k < n; k++)
                    {
                        char[] row = b.Empty();
                        if (net)
                        {
                            b.Set(row, b.Lane, b.Layer - 1, '=');
                        }
                    }

                    if (net)
                    {
                        // The net runs on under the landing and climbs back onto the route.
                        char[] r1 = b.Rail(rng.Chance(0.5f) ? 'g' : '=');
                        b.Set(r1, b.Lane, b.Layer - 1, '=');
                        char[] r2 = b.Rail();
                        b.Set(r2, b.Lane, b.Layer - 1, '^');
                    }
                    else
                    {
                        b.Rail(rng.Chance(0.5f) ? 'g' : '=');
                    }

                    return true;
                }

                case BeatKind.Shift:
                {
                    int to = Neighbour(b.Lane, rng);
                    b.Rail();
                    int overlap = tier <= 0 ? 2 : 1;
                    for (int k = 0; k < overlap; k++)
                    {
                        char[] row = b.Rail();
                        b.Set(row, to, b.Layer, '=');
                    }

                    b.Lane = to;
                    b.Rail();
                    return true;
                }

                case BeatKind.ShiftGap:
                {
                    int to = Neighbour(b.Lane, rng);
                    b.Rail();
                    char[] both = b.Rail();
                    b.Set(both, to, b.Layer, '=');
                    b.Lane = to;
                    b.Empty();
                    b.Rail('g');
                    return true;
                }

                case BeatKind.Weave:
                {
                    int mid = Neighbour(b.Lane, rng);
                    int end = mid == 1 ? 2 - b.Lane : 1; // from an edge lane: across; from the centre: back
                    b.Rail();
                    char[] a = b.Rail();
                    b.Set(a, mid, b.Layer, '=');
                    b.Lane = mid;
                    b.Rail('g');
                    char[] c = b.Rail();
                    b.Set(c, end, b.Layer, '=');
                    b.Lane = end;
                    b.Rail();
                    return true;
                }

                case BeatKind.RampUp:
                {
                    if (b.Layer >= maxLayer)
                    {
                        return false;
                    }

                    b.Rail();
                    b.Empty(); // build the ramp up here
                    b.Layer++;
                    b.Rail(rng.Chance(0.4f) ? 'g' : '=');
                    return true;
                }

                case BeatKind.Drop:
                {
                    if (b.Layer <= 0)
                    {
                        return false;
                    }

                    b.Rail();
                    char[] row = b.Empty(); // ramp down (1 fuel) or fall onto the rail below (a hull point)
                    b.Set(row, b.Lane, b.Layer - 1, '=');
                    b.Layer--;
                    b.Rail();
                    return true;
                }

                case BeatKind.RideRamp:
                {
                    bool up = b.Layer < maxLayer && (b.Layer == 0 || rng.Chance(0.5f));
                    if (!up && b.Layer <= 0)
                    {
                        return false;
                    }

                    b.Rail();
                    b.Rail(up ? '^' : 'v');
                    b.Layer += up ? 1 : -1;
                    b.Rail('g');
                    b.Rail('g');
                    return true;
                }

                case BeatKind.Fork:
                {
                    int side = Neighbour(b.Lane, rng);
                    char prize = rng.Chance(0.35f) ? 'f' : 'g';
                    b.Rail();
                    char[] r0 = b.Rail();
                    b.Set(r0, side, b.Layer, '=');
                    b.Rail(); // the branch has a gap here: bridge it for the prize
                    char[] r2 = b.Rail();
                    b.Set(r2, side, b.Layer, prize);
                    char[] r3 = b.Rail();
                    b.Set(r3, side, b.Layer, prize);
                    char[] r4 = b.Rail();
                    b.Set(r4, side, b.Layer, '=');
                    char[] r5 = b.Rail(); // rail on past the prize, so it sits in clear sight
                    b.Set(r5, side, b.Layer, '=');
                    return true;
                }

                case BeatKind.SkyRun:
                {
                    if (maxLayer < FieldSpec.TopLayer)
                    {
                        return false;
                    }

                    while (b.Layer < FieldSpec.TopLayer)
                    {
                        b.Rail();
                        b.Rail('^');
                        b.Layer++;
                    }

                    b.Rail('g');
                    b.Rail('g');
                    b.Empty();
                    b.Rail('g');
                    b.Rail('g');
                    b.Rail('v');
                    b.Layer--;
                    return true;
                }

                case BeatKind.VoidWalk:
                {
                    while (b.Layer > 0)
                    {
                        b.Rail();
                        b.Rail('v');
                        b.Layer--;
                    }

                    b.Rail();
                    b.Empty();
                    b.Rail('g');
                    if (tier >= 2)
                    {
                        b.Empty();
                        b.Rail('g');
                    }

                    b.Rail('^');
                    b.Layer++;
                    return true;
                }

                case BeatKind.Dodge:
                {
                    int side = Neighbour(b.Lane, rng);
                    b.Rail();
                    char[] r0 = b.Rail();
                    b.Set(r0, side, b.Layer, '=');
                    char[] r1 = b.Rail('#');
                    b.Set(r1, side, b.Layer, rng.Chance(0.5f) ? 'g' : '=');
                    char[] r2 = b.Rail();
                    b.Set(r2, side, b.Layer, '=');
                    if (rng.Chance(0.5f))
                    {
                        b.Lane = side; // the route carries on beside the cube's lane
                    }

                    return true;
                }

                case BeatKind.Combo:
                {
                    int parts = 2 + rng.Range(0, 2);
                    int done = 0;
                    for (int guard = 0; guard < 12 && done < parts; guard++)
                    {
                        BeatKind part = ComboParts[rng.Range(0, ComboParts.Length)];
                        var sub = new Builder(b.Lane, b.Layer);
                        if (!Write(sub, part, maxLayer, tier, 0f, rng))
                        {
                            continue;
                        }

                        b.Rows.AddRange(sub.Rows);
                        b.Lane = sub.Lane;
                        b.Layer = sub.Layer;
                        done++;
                    }

                    return done > 0;
                }

                case BeatKind.Gems:
                    b.Rail();
                    b.Rail('g');
                    b.Rail('g');
                    b.Rail('g');
                    return true;

                default: // Fuel
                    b.Rail('f');
                    b.Rail();
                    b.Rail('f');
                    return true;
            }
        }

        private static int Neighbour(int lane, Rng rng)
        {
            if (lane <= 0)
            {
                return 1;
            }

            if (lane >= FieldSpec.LaneCount - 1)
            {
                return lane - 1;
            }

            return rng.Chance(0.5f) ? lane - 1 : lane + 1;
        }

        /// <summary>Appends rows that carry the route rail where the route currently is.</summary>
        private sealed class Builder
        {
            public readonly List<char[]> Rows = new List<char[]>(16);
            public int Lane;
            public int Layer;

            public Builder(int lane, int layer)
            {
                Lane = lane;
                Layer = layer;
            }

            /// <summary>A new row with only the route rail (as <paramref name="code"/>).</summary>
            public char[] Rail(char code = '=')
            {
                char[] row = Empty();
                Set(row, Lane, Layer, code);
                return row;
            }

            /// <summary>A new row of gaps.</summary>
            public char[] Empty()
            {
                var row = new char[FieldSpec.CellsPerRow];
                for (int i = 0; i < row.Length; i++)
                {
                    row[i] = '.';
                }

                Rows.Add(row);
                return row;
            }

            public void Set(char[] row, int lane, int layer, char code)
            {
                row[FieldSpec.CellIndex(lane, layer)] = code;
            }
        }
    }
}

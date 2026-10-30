using System;
using System.Collections.Generic;

namespace BeamRunner.Core.Field
{
    /// <summary>What a chunk is mostly about (levels weight their chunk choice by tag).</summary>
    public enum ChunkTag
    {
        /// <summary>Gaps to bridge.</summary>
        Bridge = 0,

        /// <summary>Layer changes: ramps to ride or build.</summary>
        Ramp,

        /// <summary>The ceiling and the void: top-layer runs and bottom-layer gaps.</summary>
        Edge,

        /// <summary>Cubes on the rails that force a route change (level 5+).</summary>
        Obstacle,

        /// <summary>Gem trails across gaps and between layers (the greed decision).</summary>
        Gems,

        /// <summary>A breather that pays fuel.</summary>
        Fuel,
    }

    /// <summary>
    /// A hand-built stretch of track: a few tiles of the 3 × 3 rail grid with its gaps, ramps, obstacles and pickups.
    /// The generator stitches chunks together (mirrored half of the time) between stretches of plain track, and only
    /// places one the run can afford (<see cref="RouteSolver"/>). Written as text: rows (tiles) separated by commas, each
    /// an optional repeat count (<c>3x</c>) and then three groups of three cells — bottom, middle and top layer, lanes left
    /// to right — e.g. <c>"=== =.= ..."</c>. Cells: <c>.</c> gap, <c>=</c> rail, <c>^</c> ramp up, <c>v</c> ramp down,
    /// <c>#</c> obstacle, <c>o</c> optional obstacle (rail when the adaptive roll says no), <c>g</c> rail + gems,
    /// <c>*</c> gap + gems (reached only by building), <c>f</c> rail + fuel, <c>F</c> gap + fuel, <c>s</c> rail + shield,
    /// <c>r</c> rail + repair, <c>p</c> rail + a power-up when one is due (gems otherwise).
    /// </summary>
    public sealed class Chunk
    {
        public const int TagCount = 6;

        public readonly string Id;
        public readonly ChunkTag Tag;
        public readonly int Tier;

        /// <summary>Rows (tiles), each <see cref="FieldSpec.CellsPerRow"/> cell codes indexed by <see cref="FieldSpec.CellIndex"/>.</summary>
        public readonly char[][] Rows;

        /// <summary>True if any row uses the top layer (such chunks wait until the top layer opens).</summary>
        public readonly bool UsesTop;

        /// <summary>True if any row holds an obstacle (such chunks wait until level 5).</summary>
        public readonly bool HasObstacles;

        public Chunk(string id, ChunkTag tag, int tier, string rows)
        {
            Id = id;
            Tag = tag;
            Tier = tier;
            Rows = Parse(rows);
            for (int r = 0; r < Rows.Length; r++)
            {
                for (int i = 0; i < FieldSpec.CellsPerRow; i++)
                {
                    char c = Rows[r][i];
                    if (c != '.' && i >= FieldSpec.CellIndex(0, FieldSpec.TopLayer))
                    {
                        UsesTop = true;
                    }

                    if (c == '#' || c == 'o')
                    {
                        HasObstacles = true;
                    }
                }
            }
        }

        public int Length
        {
            get { return Rows.Length; }
        }

        /// <summary>Cell code at (row, lane, layer), mirrored left↔right if asked.</summary>
        public char Code(int row, int lane, int layer, bool mirror)
        {
            if (mirror)
            {
                lane = FieldSpec.LaneCount - 1 - lane;
            }

            return Rows[row][FieldSpec.CellIndex(lane, layer)];
        }

        /// <summary>The rail a cell code stands for (pickups ride on rails or float over gaps).</summary>
        public static CellType CellOf(char code, bool obstacleRoll)
        {
            switch (code)
            {
                case '=':
                case 'g':
                case 'f':
                case 's':
                case 'r':
                case 'p':
                    return CellType.Track;
                case '^':
                    return CellType.RampUp;
                case 'v':
                    return CellType.RampDown;
                case '#':
                    return CellType.Obstacle;
                case 'o':
                    return obstacleRoll ? CellType.Obstacle : CellType.Track;
                default:
                    return CellType.Empty;
            }
        }

        /// <summary>Fuel cells a code holds (before the level's fuel scale).</summary>
        public static bool HoldsFuel(char code)
        {
            return code == 'f' || code == 'F';
        }

        /// <summary>True if a code holds gems.</summary>
        public static bool HoldsGems(char code)
        {
            return code == 'g' || code == '*';
        }

        private static char[][] Parse(string text)
        {
            var rows = new List<char[]>();
            string[] parts = text.Split(',');
            for (int p = 0; p < parts.Length; p++)
            {
                string s = parts[p].Trim();
                if (s.Length == 0)
                {
                    continue;
                }

                int repeat = 1;
                int x = s.IndexOf('x');
                if (x > 0 && x < 3 && char.IsDigit(s[0]))
                {
                    repeat = int.Parse(s.Substring(0, x), System.Globalization.CultureInfo.InvariantCulture);
                    s = s.Substring(x + 1).Trim();
                }

                string[] groups = s.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (groups.Length != FieldSpec.LayerCount)
                {
                    throw new FormatException("Chunk row needs " + FieldSpec.LayerCount + " layer groups: '" + s + "'");
                }

                var row = new char[FieldSpec.CellsPerRow];
                for (int layer = 0; layer < FieldSpec.LayerCount; layer++)
                {
                    string g = groups[layer];
                    if (g.Length != FieldSpec.LaneCount)
                    {
                        throw new FormatException("Chunk layer group needs " + FieldSpec.LaneCount + " lanes: '" + g + "'");
                    }

                    for (int lane = 0; lane < FieldSpec.LaneCount; lane++)
                    {
                        if ("=.^v#ogf*Fsrp".IndexOf(g[lane]) < 0)
                        {
                            throw new FormatException("Unknown chunk cell '" + g[lane] + "' in '" + s + "'");
                        }

                        row[FieldSpec.CellIndex(lane, layer)] = g[lane];
                    }
                }

                for (int k = 0; k < repeat; k++)
                {
                    rows.Add(row);
                }
            }

            return rows.ToArray();
        }
    }
}

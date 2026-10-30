using System;
using System.Collections.Generic;

namespace BeamRunner.Core.Field
{
    /// <summary>What fills one cell of the track (one lane of one layer of one tile). Values are saved in events.</summary>
    public enum CellType : byte
    {
        /// <summary>No rail: a gap. The craft falls unless something is built here.</summary>
        Empty = 0,

        /// <summary>A straight rail.</summary>
        Track,

        /// <summary>A rail rising one layer over the tile (the craft ends the tile on the layer above).</summary>
        RampUp,

        /// <summary>A rail dropping one layer over the tile (the craft ends the tile on the layer below).</summary>
        RampDown,

        /// <summary>A rail with a cube standing on it: riding into it (or building into it) crashes.</summary>
        Obstacle,

        /// <summary>The wall that ends a level (every cell of its row): the craft is carried up and over it.</summary>
        Wall,
    }

    /// <summary>What the player can build on the next tile (all cost fuel).</summary>
    public enum BuildKind
    {
        None = 0,

        /// <summary>Tap: a straight rail on the next tile.</summary>
        Bridge,

        /// <summary>Swipe up: a ramp up one layer on the next tile.</summary>
        RampUp,

        /// <summary>Swipe down: a ramp down one layer on the next tile.</summary>
        RampDown,
    }

    /// <summary>Static helpers for <see cref="CellType"/>.</summary>
    public static class Cells
    {
        /// <summary>True if the craft can ride (or land on) the cell: anything but a gap. Obstacles crash, but hold a rail.</summary>
        public static bool Solid(CellType c)
        {
            return c != CellType.Empty;
        }

        /// <summary>True for a ramp.</summary>
        public static bool IsRamp(CellType c)
        {
            return c == CellType.RampUp || c == CellType.RampDown;
        }

        /// <summary>Layers a cell moves the craft over its tile: +1, −1 or 0.</summary>
        public static int LayerDelta(CellType c)
        {
            return c == CellType.RampUp ? 1 : c == CellType.RampDown ? -1 : 0;
        }

        /// <summary>The cell a build creates.</summary>
        public static CellType FromBuild(BuildKind kind)
        {
            switch (kind)
            {
                case BuildKind.RampUp:
                    return CellType.RampUp;
                case BuildKind.RampDown:
                    return CellType.RampDown;
                case BuildKind.Bridge:
                    return CellType.Track;
                default:
                    return CellType.Empty;
            }
        }
    }

    /// <summary>One tile of track: 3 lanes × 3 layers of cells, plus which level it belongs to.</summary>
    public sealed class TrackRow
    {
        public readonly CellType[] Cells = new CellType[FieldSpec.CellsPerRow];

        /// <summary>Seconds (run time) each cell was built by the player; negative = part of the generated track.</summary>
        public readonly float[] BuiltAt = new float[FieldSpec.CellsPerRow];

        /// <summary>Fuel the generator placed on each cell (planning hint; not updated when collected).</summary>
        public readonly float[] Fuel = new float[FieldSpec.CellsPerRow];

        /// <summary>Gems the generator placed on each cell (planning hint; not updated when collected).</summary>
        public readonly byte[] Gems = new byte[FieldSpec.CellsPerRow];

        /// <summary>Tile index (0 = the run's first tile).</summary>
        public long Tile;

        /// <summary>Level the tile belongs to (1-based).</summary>
        public int Level;

        /// <summary>True for the level's closing wall.</summary>
        public bool IsWall;

        public CellType Get(int lane, int layer)
        {
            if (lane < 0 || lane >= FieldSpec.LaneCount || layer < 0 || layer >= FieldSpec.LayerCount)
            {
                return CellType.Empty;
            }

            return Cells[FieldSpec.CellIndex(lane, layer)];
        }

        public void Set(int lane, int layer, CellType type)
        {
            Cells[FieldSpec.CellIndex(lane, layer)] = type;
        }

        /// <summary>True if the player built the cell (rendered with the build glow).</summary>
        public bool IsBuilt(int lane, int layer)
        {
            return BuiltAt[FieldSpec.CellIndex(lane, layer)] >= 0f;
        }

        internal void Reset(long tile, int level)
        {
            Tile = tile;
            Level = level;
            IsWall = false;
            for (int i = 0; i < Cells.Length; i++)
            {
                Cells[i] = CellType.Empty;
                BuiltAt[i] = -1f;
                Fuel[i] = 0f;
                Gems[i] = 0;
            }
        }
    }

    /// <summary>
    /// The generated track around the craft: a window of consecutive <see cref="TrackRow"/>s by tile index. The generator
    /// appends rows ahead, the simulation drops them behind and writes the player's builds into them. Rows are pooled.
    /// </summary>
    public sealed class TrackGrid
    {
        private readonly List<TrackRow> _rows = new List<TrackRow>(64);
        private readonly Stack<TrackRow> _pool = new Stack<TrackRow>(64);
        private long _first;

        /// <summary>Raised when a cell changes after generation (a build, a smashed obstacle): (tile, lane, layer).</summary>
        public event Action<long, int, int> CellChanged;

        /// <summary>First tile held.</summary>
        public long FirstTile
        {
            get { return _first; }
        }

        /// <summary>One past the last tile held (the next tile the generator writes).</summary>
        public long EndTile
        {
            get { return _first + _rows.Count; }
        }

        public int Count
        {
            get { return _rows.Count; }
        }

        /// <summary>Row of <paramref name="tile"/>, or null outside the window.</summary>
        public TrackRow Row(long tile)
        {
            long i = tile - _first;
            return i >= 0 && i < _rows.Count ? _rows[(int)i] : null;
        }

        /// <summary>Row by position in the window (0 = <see cref="FirstTile"/>).</summary>
        public TrackRow RowAt(int index)
        {
            return _rows[index];
        }

        /// <summary>Cell at (tile, lane, layer); outside the grid (or the window) it is a gap.</summary>
        public CellType Get(long tile, int lane, int layer)
        {
            TrackRow row = Row(tile);
            return row != null ? row.Get(lane, layer) : CellType.Empty;
        }

        /// <summary>Changes a cell after generation (a build or a smash) and raises <see cref="CellChanged"/>.</summary>
        public void Set(long tile, int lane, int layer, CellType type, float builtAt = -1f)
        {
            TrackRow row = Row(tile);
            if (row == null || lane < 0 || lane >= FieldSpec.LaneCount || layer < 0 || layer >= FieldSpec.LayerCount)
            {
                return;
            }

            int i = FieldSpec.CellIndex(lane, layer);
            row.Cells[i] = type;
            if (builtAt >= 0f)
            {
                row.BuiltAt[i] = builtAt;
            }

            CellChanged?.Invoke(tile, lane, layer);
        }

        /// <summary>Appends a fresh (all gaps) row for the next tile.</summary>
        public TrackRow Append(int level)
        {
            TrackRow row = _pool.Count > 0 ? _pool.Pop() : new TrackRow();
            row.Reset(EndTile, level);
            _rows.Add(row);
            return row;
        }

        /// <summary>Drops (and recycles) every row before <paramref name="tile"/>.</summary>
        public void DropBefore(long tile)
        {
            int n = 0;
            while (n < _rows.Count && _rows[n].Tile < tile)
            {
                _pool.Push(_rows[n]);
                n++;
            }

            if (n > 0)
            {
                _rows.RemoveRange(0, n);
                _first += n;
            }
        }
    }
}

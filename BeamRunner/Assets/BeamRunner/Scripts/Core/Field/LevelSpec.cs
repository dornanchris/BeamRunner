namespace BeamRunner.Core.Field
{
    /// <summary>
    /// Everything that defines one level: its theme, speed ramp, length in tiles and which hand-built chunks it may stitch
    /// together (tier cap and tag weights). Built by <see cref="Levels.Get"/>; gem value comes from the level number
    /// through <see cref="Balance.BalanceConfig"/>.
    /// </summary>
    public sealed class LevelSpec
    {
        /// <summary>1-based level number.</summary>
        public int Number;

        public LevelKind Kind;

        /// <summary>0 at level 1, 1 from the plateau level on.</summary>
        public float Tier;

        /// <summary>Planned speed (m/s) at the level's start and end (linear in distance in between).</summary>
        public float StartSpeed;

        public float EndSpeed;

        /// <summary>Tiles in the level, its intro and closing wall included.</summary>
        public int Tiles;

        /// <summary>Course tile where the level starts.</summary>
        public long StartTile;

        /// <summary>Hardest chunk tier the level may use (chunks unlock as the level rises).</summary>
        public int MaxChunkTier;

        /// <summary>Tiles of plain track between two chunks (before the adaptive gap multiplier).</summary>
        public float ChunkGap = 2f;

        /// <summary>Relative chance of a chunk of each tag (indexed by <see cref="ChunkTag"/>).</summary>
        public float[] TagWeights = new float[Chunk.TagCount];

        /// <summary>False before the top layer opens (level 1 rides the middle and bottom layers only).</summary>
        public bool TopLayer = true;

        /// <summary>False before obstacles appear (level 5).</summary>
        public bool Obstacles;

        /// <summary>Fuel cells written in the chunks that are kept (1 = all; below 1 = scarcer fuel).</summary>
        public float FuelScale = 1f;

        /// <summary>String-table key of the level's name ("level.bridges" …).</summary>
        public string NameKey
        {
            get { return Levels.NameKey(Kind); }
        }

        /// <summary>One past the level's last tile (its wall is the tile before).</summary>
        public long EndTile
        {
            get { return StartTile + Tiles; }
        }

        /// <summary>The level's closing wall.</summary>
        public long WallTile
        {
            get { return EndTile - 1; }
        }

        /// <summary>Course position (m) of the level's start.</summary>
        public double Start
        {
            get { return FieldSpec.TileStart(StartTile); }
        }

        /// <summary>Length on the course (m).</summary>
        public double Length
        {
            get { return Tiles * (double)FieldSpec.TileLength; }
        }

        public double End
        {
            get { return Start + Length; }
        }
    }
}

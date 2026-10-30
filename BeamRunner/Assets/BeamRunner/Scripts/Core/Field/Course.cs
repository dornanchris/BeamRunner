namespace BeamRunner.Core.Field
{
    /// <summary>
    /// Maps a run's distance (0 at its start line) onto the level course, so a run can start at any level:
    /// course position = distance + <see cref="Offset"/>, course tile = run tile + <see cref="TileOffset"/>.
    /// </summary>
    public sealed class Course
    {
        public Course(int startLevel)
        {
            StartLevel = startLevel < 1 ? 1 : startLevel;
            TileOffset = Levels.StartTileOf(StartLevel);
            Offset = FieldSpec.TileStart(TileOffset);
        }

        /// <summary>Level the run started at (1-based).</summary>
        public int StartLevel { get; private set; }

        /// <summary>Course position of the run's start line.</summary>
        public double Offset { get; private set; }

        /// <summary>Course tile of the run's first tile.</summary>
        public long TileOffset { get; private set; }

        public int LevelAt(double distance)
        {
            return Levels.LevelAt(distance + Offset);
        }

        /// <summary>Level of run tile <paramref name="tile"/>.</summary>
        public int LevelOfTile(long tile)
        {
            return Levels.LevelOfTile(tile + TileOffset);
        }

        public LevelSpec SpecAt(double distance)
        {
            return Levels.Get(LevelAt(distance));
        }

        /// <summary>Run tile where level <paramref name="number"/> starts.</summary>
        public long LevelStartTile(int number)
        {
            return Levels.StartTileOf(number) - TileOffset;
        }

        /// <summary>Run tile of level <paramref name="number"/>'s closing wall.</summary>
        public long WallTile(int number)
        {
            return Levels.StartTileOf(number + 1) - TileOffset - 1;
        }

        /// <summary>Run distance at which level <paramref name="number"/> starts.</summary>
        public double LevelStart(int number)
        {
            return Levels.StartOf(number) - Offset;
        }

        /// <summary>Run distance at which level <paramref name="number"/> ends.</summary>
        public double LevelEnd(int number)
        {
            return Levels.StartOf(number + 1) - Offset;
        }

        /// <summary>0..1 progress through the level at <paramref name="distance"/>.</summary>
        public float Progress01(double distance)
        {
            int n = LevelAt(distance);
            double start = LevelStart(n);
            double end = LevelEnd(n);
            return MathUtil.Clamp01((float)((distance - start) / (end - start)));
        }

        /// <summary>Planned speed (m/s) at run distance <paramref name="distance"/> (before the adaptive multiplier).</summary>
        public float SpeedAt(double distance)
        {
            return Levels.SpeedAt(distance + Offset);
        }
    }
}

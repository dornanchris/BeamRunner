namespace BeamRunner.Core.Field
{
    /// <summary>
    /// Where the roadside brand sign of each level stands (scenery only: nothing collides with it): one per level,
    /// <see cref="Offset"/> m after the level's start, on alternating sides from level to level. Every level is longer
    /// than <see cref="Offset"/> + <see cref="WallClearance"/>, so no sign stands in a bend up a wall.
    /// </summary>
    public static class Signs
    {
        /// <summary>Distance (m) from a level's start to its sign: a run starts with it ahead, not alongside.</summary>
        public const double Offset = 96.0;

        /// <summary>No sign stands within this many metres before a level's wall tile.</summary>
        public const double WallClearance = 56.0;

        /// <summary>Run distance of level <paramref name="number"/>'s sign, or NaN if it has none.</summary>
        public static double At(Course course, int number)
        {
            if (course == null)
            {
                return double.NaN;
            }

            double s = course.LevelStart(number) + Offset;
            return s <= FieldSpec.TileStart(course.WallTile(number)) - WallClearance ? s : double.NaN;
        }

        /// <summary>Side of level <paramref name="number"/>'s sign: +1 right of the track, −1 left.</summary>
        public static int SideOf(int number)
        {
            return (number & 1) == 0 ? -1 : 1;
        }
    }
}

using BeamRunner.Core.Field;
using NUnit.Framework;

namespace BeamRunner.Core.Tests
{
    public class SignsTests
    {
        [TestCase(1)]
        [TestCase(4)]
        [TestCase(13)]
        public void EveryLevelHasOneSign_ClearOfItsBends(int startLevel)
        {
            var course = new Course(startLevel);
            for (int level = startLevel; level < startLevel + 20; level++)
            {
                double s = Signs.At(course, level);
                Assert.That(double.IsNaN(s), Is.False, "level " + level);
                Assert.That(s, Is.EqualTo(course.LevelStart(level) + Signs.Offset).Within(1e-9), "level " + level);
                Assert.That(s, Is.LessThanOrEqualTo(FieldSpec.TileStart(course.WallTile(level)) - Signs.WallClearance),
                    "level " + level);
            }
        }

        [Test]
        public void SidesAlternate_FromLevelToLevel()
        {
            Assert.That(Signs.SideOf(1), Is.EqualTo(-Signs.SideOf(2)));
            Assert.That(System.Math.Abs(Signs.SideOf(7)), Is.EqualTo(1));
        }

        [Test]
        public void NoCourse_HasNoSign()
        {
            Assert.That(double.IsNaN(Signs.At(null, 1)), Is.True);
        }
    }
}

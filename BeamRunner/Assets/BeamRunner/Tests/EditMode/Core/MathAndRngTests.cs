using System;
using BeamRunner.Core;
using BeamRunner.Core.Field;
using NUnit.Framework;

namespace BeamRunner.Core.Tests
{
    public class MathAndRngTests
    {
        [Test]
        public void WrapAngle_StaysInRange()
        {
            float[] samples = { -100f, -MathUtil.TwoPi, -0.001f, 0f, 1f, MathUtil.TwoPi, 7f, 1000f };
            foreach (float s in samples)
            {
                float w = MathUtil.WrapAngle(s);
                Assert.That(w, Is.GreaterThanOrEqualTo(0f).And.LessThan(MathUtil.TwoPi), "input " + s);
            }
        }

        [Test]
        public void DeltaAngle_TakesShortestPath()
        {
            Assert.That(MathUtil.DeltaAngle(0.1f, MathUtil.TwoPi - 0.1f), Is.EqualTo(-0.2f).Within(1e-4f));
            Assert.That(MathUtil.DeltaAngle(MathUtil.TwoPi - 0.1f, 0.1f), Is.EqualTo(0.2f).Within(1e-4f));
            Assert.That(MathUtil.AngleDistance(1f, 1f + MathUtil.TwoPi * 3f), Is.EqualTo(0f).Within(1e-3f));
        }

        [Test]
        public void AngleInArc_HandlesWrapAround()
        {
            float start = MathUtil.TwoPi - 0.2f;
            Assert.IsTrue(MathUtil.AngleInArc(0.1f, start, 0.4f));
            Assert.IsTrue(MathUtil.AngleInArc(MathUtil.TwoPi - 0.1f, start, 0.4f));
            Assert.IsFalse(MathUtil.AngleInArc(0.3f, start, 0.4f));
        }

        [Test]
        public void ArcClearance_SignMatchesOverlap()
        {
            // Arc [1, 2]; ship half-width 0.1.
            Assert.That(MathUtil.ArcClearance(0.5f, 0.1f, 1f, 1f), Is.EqualTo(0.4f).Within(1e-4f));
            Assert.That(MathUtil.ArcClearance(1.05f, 0.1f, 1f, 1f), Is.LessThan(0f));
            Assert.That(MathUtil.ArcClearance(2.3f, 0.1f, 1f, 1f), Is.EqualTo(0.2f).Within(1e-4f));
        }

        [Test]
        public void Rng_IsDeterministicAndInRange()
        {
            var a = new Rng(1234);
            var b = new Rng(1234);
            var c = new Rng(1235);
            bool anyDifferent = false;
            for (int i = 0; i < 1000; i++)
            {
                float va = a.Value();
                Assert.AreEqual(va, b.Value());
                if (Math.Abs(va - c.Value()) > 1e-6f)
                {
                    anyDifferent = true;
                }

                Assert.That(va, Is.GreaterThanOrEqualTo(0f).And.LessThan(1f));
                int r = a.Range(3, 9);
                b.Range(3, 9);
                c.Range(3, 9);
                Assert.That(r, Is.InRange(3, 8));
            }

            Assert.IsTrue(anyDifferent, "nearby seeds should diverge");
        }

        [Test]
        public void Rng_WeightedRespectsZeroWeights()
        {
            var rng = new Rng(7);
            float[] w = { 0f, 1f, 0f, 2f };
            for (int i = 0; i < 500; i++)
            {
                int pick = rng.Weighted(w);
                Assert.That(pick == 1 || pick == 3);
            }
        }

        [Test]
        public void ColorHex_RoundTrips()
        {
            ColorRGB c = ColorRGB.Hex("#FF2BD6");
            Assert.AreEqual("#FF2BD6", c.ToHex());
            Assert.AreEqual(1f, c.A);
            Assert.That(ColorRGB.Hex("00E5FF80").A, Is.EqualTo(128f / 255f).Within(1e-4f));
        }

        [Test]
        public void LevelTiles_AreContiguous_AndMapBackToTheirLevel()
        {
            for (int n = 1; n <= Levels.PlateauLevel + 6; n++)
            {
                long start = Levels.StartTileOf(n);
                Assert.AreEqual(start + Levels.TilesOf(n), Levels.StartTileOf(n + 1), "level " + n + " ends where the next starts");
                Assert.AreEqual(n, Levels.LevelOfTile(start), "first tile of level " + n);
                Assert.AreEqual(n, Levels.LevelOfTile(Levels.StartTileOf(n + 1) - 1), "wall tile of level " + n);
                Assert.AreEqual(n, Levels.LevelAt(Levels.StartOf(n) + 0.5), "course position in level " + n);
                Assert.AreEqual(start, Levels.Get(n).StartTile);
                Assert.AreEqual(Levels.StartTileOf(n + 1) - 1, Levels.Get(n).WallTile);
            }

            var course = new Course(4);
            Assert.AreEqual(0, course.LevelStartTile(4), "a run started at level 4 begins on its first tile");
            Assert.AreEqual(Levels.TilesOf(4) - 1, course.WallTile(4));
            Assert.AreEqual(5, course.LevelOfTile(Levels.TilesOf(4)));
        }

        [Test]
        public void Speed_IsContinuousMonotonicAndPlateaus()
        {
            float prevSpeed = Levels.StartSpeed;
            double end = Levels.StartOf(Levels.PlateauLevel + 3);
            for (double z = 0; z <= end; z += 5)
            {
                float s = Levels.SpeedAt(z);
                Assert.That(s, Is.GreaterThanOrEqualTo(prevSpeed - 1e-4f), "monotonic at " + z);
                Assert.That(s - prevSpeed, Is.LessThan(0.2f), "no speed jump at " + z);
                Assert.That(s, Is.InRange(Levels.StartSpeed, Levels.MaxSpeed));
                prevSpeed = s;
            }

            Assert.AreEqual(Levels.MaxSpeed, Levels.SpeedAt(Levels.StartOf(Levels.PlateauLevel) + 1), 1e-4f);
            Assert.AreEqual(Levels.MaxSpeed, Levels.SpeedAt(Levels.StartOf(Levels.PlateauLevel + 20)), 1e-4f);
        }

        [Test]
        public void ZoneThemes_CycleByLevel()
        {
            Assert.AreSame(ZoneThemes.Get(0), ZoneThemes.Get(ZoneThemes.Count));
            Assert.AreSame(ZoneThemes.Get(0), ZoneThemes.ForLevel(1));
            Assert.AreSame(ZoneThemes.Get(3), ZoneThemes.ForLevel(4 + ZoneThemes.Count));
            Assert.AreNotSame(ZoneThemes.ForLevel(1), ZoneThemes.ForLevel(2), "neighbouring levels look different");
        }
    }
}

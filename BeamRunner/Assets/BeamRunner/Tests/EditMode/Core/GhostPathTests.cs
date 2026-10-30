using System;
using System.Collections.Generic;
using BeamRunner.Core.Diagnostics;
using BeamRunner.Core.Field;
using BeamRunner.Core.Meta;
using BeamRunner.Core.Sim;
using NUnit.Framework;

namespace BeamRunner.Core.Tests
{
    public class GhostPathTests
    {
        private const int Seed = 424242;
        private const float HalfStep = (float)(FieldSpec.HalfExtent / GhostPath.PositionSteps);

        private struct Frame
        {
            public double Distance;
            public float X;
            public float Y;
            public double Time;
        }

        // ------------------------------------------------------------------ recording & quantisation

        [Test]
        public void Samples_AreWithinHalfAQuantisationStep_WithoutDrift()
        {
            // Observations exactly on the sample grid: the only error left is the quantisation.
            Func<double, double> fx = d => 3.5 * Math.Sin(d / 23.0);
            Func<double, double> fy = d => 3.0 * Math.Cos(d / 31.0) + 0.8 * Math.Sin(d / 7.0);
            GhostPath g = Record(fx, fy, 16500.0, 2.0, 2.0 / 40.0);
            Assert.AreEqual(GhostPath.MaxSamples, g.SampleCount, "16.5 km fills the cap");
            for (int k = 0; k < g.SampleCount; k++)
            {
                Assert.That(Math.Abs(g.SampleX(k) - fx(k * 2.0)), Is.LessThanOrEqualTo(HalfStep + 1e-4), "x sample " + k);
                Assert.That(Math.Abs(g.SampleY(k) - fy(k * 2.0)), Is.LessThanOrEqualTo(HalfStep + 1e-4), "y sample " + k);
            }
        }

        [Test]
        public void Positions_OutsideTheField_AreClamped()
        {
            Assert.AreEqual(0, GhostPath.Quantise(-50f));
            Assert.AreEqual(GhostPath.PositionSteps, GhostPath.Quantise(50f));
            Assert.AreEqual(-FieldSpec.HalfExtent, GhostPath.Dequantise(0), 1e-5f);
            Assert.AreEqual(FieldSpec.HalfExtent, GhostPath.Dequantise(GhostPath.PositionSteps), 1e-5f);
            Assert.AreEqual(0f, GhostPath.Dequantise(GhostPath.Quantise(0f)), HalfStep + 1e-5f);
        }

        [Test]
        public void Recording_IsIndependentOfTheFrameRate()
        {
            Func<double, double> fx = d => 3.5 * Math.Sin(d / 14.0);
            Func<double, double> fy = d => -3.0 * Math.Cos(d / 9.5);
            GhostPath slow = Record(fx, fy, 3000.0, 40.0 / 30.0, 1.0 / 30.0); // 30 fps at 40 m/s
            GhostPath fast = Record(fx, fy, 3000.0, 40.0 / 120.0, 1.0 / 120.0); // 120 fps
            Assert.AreEqual(slow.SampleCount, fast.SampleCount);
            for (double d = 0.0; d <= slow.Length; d += 1.7)
            {
                float sx;
                float sy;
                float fx2;
                float fy2;
                slow.PositionAt(d, out sx, out sy);
                fast.PositionAt(d, out fx2, out fy2);
                Assert.That(Math.Abs(sx - fx2), Is.LessThan(0.12), "x at " + d);
                Assert.That(Math.Abs(sy - fy2), Is.LessThan(0.12), "y at " + d);
                Assert.AreEqual(slow.TimeAt(d), fast.TimeAt(d), 0.002, "time at " + d);
            }
        }

        [Test]
        public void RealDailyRun_GhostRetracesTheCraft()
        {
            int day = DailyRuns.DayIndex(new DateTime(2026, 9, 26, 0, 0, 0, DateTimeKind.Utc));
            var sim = new RunSimulation(new RunConfig
            {
                Seed = DailyRuns.SeedForDay(day),
                MaxRevives = 0,
                Invincible = true,
                AutoCheckpoint = true,
            });
            var rec = new GhostRecorder(sim.Config.Seed, DailyRuns.GeneratorVersion);
            var frames = new List<Frame>();
            var offRails = new List<double>();
            rec.Record(sim.Distance, sim.ShipX, sim.ShipY, sim.Time);
            for (int i = 0; i < 60 * 600 && sim.Distance < 5000.0 && !sim.IsCrashed; i++)
            {
                sim.Step(1f / 60f, RunAutopilot.FollowLine(sim)); // builds, rides ramps, climbs walls, grabs Overload
                sim.ClearEvents();
                rec.Record(sim.Distance, sim.ShipX, sim.ShipY, sim.Time);
                frames.Add(new Frame { Distance = sim.Distance, X = sim.ShipX, Y = sim.ShipY, Time = sim.Time });
                if (sim.Phase != RunPhase.Riding || Math.Abs(sim.ShipY) > FieldSpec.HalfExtent)
                {
                    offRails.Add(sim.Distance);
                }
            }

            Assert.IsFalse(sim.IsCrashed, "an invincible run flies 5 km");
            GhostPath g = GhostPath.Decode(rec.ToPath().Encode());
            Assert.IsNotNull(g);
            Assert.AreEqual(sim.Config.Seed, g.Seed);
            Assert.AreEqual(DailyRuns.GeneratorVersion, g.TrackVersion);
            Assert.That(g.Length, Is.GreaterThan(4990.0));

            // Wall climbs (carried above the field, then stopped at the checkpoint) and falls leave the quantised range and
            // change speed abruptly, so the ghost is held to the craft on the rails only, a little away from those moments
            // (two time segments).
            double position = 0.0;
            double positionAt = 0.0;
            double time = 0.0;
            double distance = 0.0;
            int compared = 0;
            double margin = 2.25 * GhostPath.TimeCheckpointEvery * GhostPath.SampleSpacing;
            foreach (Frame fr in frames)
            {
                if (fr.Distance > g.Length || NearAny(offRails, fr.Distance, margin))
                {
                    continue;
                }

                compared++;

                float x;
                float y;
                g.PositionAt(fr.Distance, out x, out y);
                double e = Math.Sqrt((x - fr.X) * (x - fr.X) + (y - fr.Y) * (y - fr.Y));
                if (e > position)
                {
                    position = e;
                    positionAt = fr.Distance;
                }

                time = Math.Max(time, Math.Abs(g.TimeAt(fr.Distance) - fr.Time));
                distance = Math.Max(distance, Math.Abs(g.DistanceAt(fr.Time) - fr.Distance));
            }

            // Lane switches are sharp (a whole lane in about 2.3 m of track at the level-1 speed, samples every 2 m): the
            // ghost stays within about the craft's collision radius.
            Assert.That(position, Is.LessThan(0.65), "position error (m) " + position + " at " + positionAt.ToString("F1") + " m");
            Assert.That(time, Is.LessThan(0.05), "time error (s) " + time); // Overload bursts change the speed often
            Assert.That(distance, Is.LessThan(1.5), "distance error (m) " + distance);
            Assert.That(g.EncodedSize, Is.LessThan(5000 * 1.13 + 32), "about 1.125 bytes per metre");
            Assert.That(compared, Is.GreaterThan(frames.Count / 2), "most of the run is on the rails");
        }

        /// <summary>
        /// True if <paramref name="sorted"/> (ascending) holds a value within <paramref name="margin"/> of <paramref name="value"/>.
        /// </summary>
        private static bool NearAny(List<double> sorted, double value, double margin)
        {
            int i = sorted.BinarySearch(value);
            if (i >= 0)
            {
                return true;
            }

            i = ~i;
            return i < sorted.Count && sorted[i] - value < margin || i > 0 && value - sorted[i - 1] < margin;
        }

        // ------------------------------------------------------------------ time track

        [Test]
        public void TimeAndDistance_AreMonotonicInverses()
        {
            // Speed varies 20 → 60 m/s (like Overload kicking in and out).
            var rec = new GhostRecorder(Seed, 1);
            double t = 0.0;
            double d = 0.0;
            rec.Record(0.0, 0f, 0f, 0.0);
            while (d < 3000.0)
            {
                double v = 40.0 + 20.0 * Math.Sin(t * 0.7);
                t += 1.0 / 60.0;
                d += v / 60.0;
                rec.Record(d, (float)Math.Sin(d * 0.01) * 5f, 0f, t);
            }

            GhostPath g = rec.ToPath();
            double previous = -1.0;
            for (double x = 0.0; x <= g.Length; x += 0.37)
            {
                double at = g.TimeAt(x);
                Assert.That(at, Is.GreaterThanOrEqualTo(previous));
                previous = at;
                Assert.AreEqual(x, g.DistanceAt(at), 0.02, "DistanceAt(TimeAt(x)) at " + x);
            }

            Assert.AreEqual(0.0, g.DistanceAt(-1.0));
            Assert.AreEqual(g.Length, g.DistanceAt(g.Duration + 5.0));
            Assert.IsTrue(g.IsFinished(g.Duration + 0.001));
            Assert.IsFalse(g.IsFinished(g.Duration * 0.5));
            float x0;
            float y0;
            float xBefore;
            float yBefore;
            g.PositionAt(0.0, out x0, out y0);
            g.PositionAt(-50.0, out xBefore, out yBefore);
            Assert.AreEqual(x0, xBefore);
            Assert.AreEqual(y0, yBefore);
        }

        // ------------------------------------------------------------------ format & size

        [Test]
        public void Encoding_RoundTripsExactly()
        {
            GhostPath g = Record(d => 4.0 * Math.Sin(d / 7.0), d => 2.0 * Math.Cos(d / 5.0), 777.0, 1.3, 1.3 / 40.0,
                -123456789, 3);
            string text = g.Encode();
            GhostPath back = GhostPath.Decode(text);
            Assert.IsNotNull(back);
            Assert.AreEqual(text, back.Encode());
            CollectionAssert.AreEqual(g.ToBytes(), back.ToBytes());
            Assert.AreEqual(-123456789, back.Seed);
            Assert.AreEqual(3, back.TrackVersion);
            Assert.AreEqual(g.SampleCount, back.SampleCount);
            Assert.AreEqual(g.Duration, back.Duration);
            for (double d = 0.0; d <= g.Length; d += 3.3)
            {
                float ax;
                float ay;
                float bx;
                float by;
                g.PositionAt(d, out ax, out ay);
                back.PositionAt(d, out bx, out by);
                Assert.AreEqual(ax, bx);
                Assert.AreEqual(ay, by);
                Assert.AreEqual(g.TimeAt(d), back.TimeAt(d));
            }
        }

        [Test]
        public void Size_FollowsTheFormula_AndIsCapped()
        {
            Assert.AreEqual(0, GhostPath.EncodedBytes(1));
            Assert.AreEqual(8 + 2 * 2 + 2 * 2, GhostPath.EncodedBytes(2));
            Assert.AreEqual(8 + 2 * 9 + 2 * 2, GhostPath.EncodedBytes(9));
            Assert.AreEqual(8 + 2 * 10 + 2 * 3, GhostPath.EncodedBytes(10));
            Assert.AreEqual(18442, GhostPath.MaxEncodedBytes, "16.4 km cap");
            Assert.AreEqual(24592, GhostPath.MaxEncodedChars);
            Assert.That(GhostPath.MaxEncodedChars, Is.LessThan(28 * 1024), "no larger than Neo Slipstream's ghost cap");

            // 20 km: the recording stops at the cap and still encodes within the limit.
            var rec = new GhostRecorder(Seed, 1);
            rec.Record(0.0, 0f, 0f, 0.0);
            double t = 0.0;
            for (double d = 1.7; d < 20000.0; d += 1.7)
            {
                t += 0.04;
                rec.Record(d, (float)Math.Sin(d / 30.0) * 8f, (float)Math.Cos(d / 40.0) * 8f, t);
            }

            Assert.IsTrue(rec.IsFull);
            GhostPath capped = rec.ToPath();
            Assert.AreEqual(GhostPath.MaxSamples, capped.SampleCount);
            Assert.AreEqual((GhostPath.MaxSamples - 1) * 2.0, capped.Length);
            Assert.AreEqual(GhostPath.MaxEncodedBytes, capped.EncodedSize);
            Assert.AreEqual(GhostPath.MaxEncodedChars, capped.Encode().Length);
            Assert.IsNotNull(GhostPath.Decode(capped.Encode()));
        }

        [Test]
        public void Decode_RejectsDamagedData()
        {
            GhostPath g = Record(d => d * 0.01, d => -d * 0.01, 300.0, 1.0, 0.02);
            byte[] good = g.ToBytes();
            Assert.IsNull(GhostPath.Decode(null));
            Assert.IsNull(GhostPath.Decode(string.Empty));
            Assert.IsNull(GhostPath.Decode("this is not base64!"));
            Assert.IsNull(GhostPath.Decode(new string('A', GhostPath.MaxEncodedChars + 4)));
            Assert.IsNull(GhostPath.FromBytes(new byte[5]));

            byte[] truncated = new byte[good.Length - 1];
            Array.Copy(good, truncated, truncated.Length);
            Assert.IsNull(GhostPath.FromBytes(truncated));

            byte[] version = (byte[])good.Clone();
            version[0] = 99;
            Assert.IsNull(GhostPath.FromBytes(version));

            byte[] count = (byte[])good.Clone();
            count[6] = (byte)(count[6] + 1);
            Assert.IsNull(GhostPath.FromBytes(count));

            Assert.IsNotNull(GhostPath.FromBytes(good));
        }

        [Test]
        public void Recorder_IgnoresStallsAndBadInput_AndNeedsTwoSamples()
        {
            var rec = new GhostRecorder(Seed, 1);
            Assert.IsNull(rec.ToPath());
            rec.Record(0.0, 0.5f, -1f, 0.0);
            rec.Record(1.0, 0.5f, -1f, 0.1);
            Assert.IsNull(rec.ToPath(), "under 2 m there is only one sample");
            rec.Record(1.0, 3f, 3f, 0.2);          // no progress (crash / pause)
            rec.Record(double.NaN, 0f, 0f, 0.3);
            rec.Record(2.5, float.NaN, 0f, 0.3);
            rec.Record(2.5, 0f, float.PositiveInfinity, 0.3);
            rec.Record(0.5, 0f, 0f, 0.4);          // backwards
            rec.Record(4.0, 0.5f, -1f, 0.4);
            GhostPath g = rec.ToPath();
            Assert.IsNotNull(g);
            Assert.AreEqual(3, g.SampleCount);
            float x;
            float y;
            g.PositionAt(3.0, out x, out y);
            Assert.AreEqual(0.5f, x, HalfStep + 1e-4f);
            Assert.AreEqual(-1f, y, HalfStep + 1e-4f);
        }

        [Test]
        public void Recorder_StartingLate_AssumesTheStartAtDistanceZero()
        {
            var rec = new GhostRecorder(Seed, 1);
            rec.Record(5.0, 2.25f, 1f, 0.14);
            rec.Record(10.0, 2.25f, 1f, 0.28);
            GhostPath g = rec.ToPath();
            Assert.AreEqual(6, g.SampleCount);
            Assert.AreEqual(0.0, g.TimeAt(0.0), 1e-9);
            float x;
            float y;
            g.PositionAt(4.0, out x, out y);
            Assert.AreEqual(2.25f, x, HalfStep + 1e-4f);
            Assert.AreEqual(1f, y, HalfStep + 1e-4f);
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>Records (fx, fy) observed every <paramref name="stepDistance"/> m / <paramref name="stepTime"/> s.</summary>
        private static GhostPath Record(Func<double, double> fx, Func<double, double> fy, double length, double stepDistance,
            double stepTime, int seed = Seed, int version = 1)
        {
            var rec = new GhostRecorder(seed, version);
            rec.Record(0.0, (float)fx(0.0), (float)fy(0.0), 0.0);
            int i = 0;
            double d = 0.0;
            while (d < length)
            {
                i++;
                d = i * stepDistance;
                rec.Record(d, (float)fx(d), (float)fy(d), i * stepTime);
            }

            return rec.ToPath();
        }
    }
}

using System;

namespace BeamRunner.Core
{
    /// <summary>Angle and interpolation helpers shared by the simulation and presentation.</summary>
    public static class MathUtil
    {
        public const float Pi = (float)Math.PI;
        public const float TwoPi = (float)(Math.PI * 2.0);
        public const float HalfPi = (float)(Math.PI * 0.5);
        public const float Deg2Rad = (float)(Math.PI / 180.0);
        public const float Rad2Deg = (float)(180.0 / Math.PI);

        /// <summary>Wraps an angle into [0, 2π).</summary>
        public static float WrapAngle(float a)
        {
            a %= TwoPi;
            if (a < 0f)
            {
                a += TwoPi;
            }

            // Guard against a == TwoPi after float rounding.
            return a >= TwoPi ? 0f : a;
        }

        /// <summary>Signed shortest difference b − a, in (−π, π].</summary>
        public static float DeltaAngle(float a, float b)
        {
            float d = WrapAngle(b - a);
            return d > Pi ? d - TwoPi : d;
        }

        /// <summary>Absolute shortest angular distance between two angles, in [0, π].</summary>
        public static float AngleDistance(float a, float b)
        {
            return Math.Abs(DeltaAngle(a, b));
        }

        /// <summary>
        /// True if angle <paramref name="a"/> lies on the arc that starts at <paramref name="start"/>
        /// and extends counter-clockwise (increasing θ) by <paramref name="width"/>.
        /// </summary>
        public static bool AngleInArc(float a, float start, float width)
        {
            if (width >= TwoPi)
            {
                return true;
            }

            return WrapAngle(a - start) <= width;
        }

        /// <summary>
        /// Signed clearance between the interval [center − halfWidth, center + halfWidth] and an arc.
        /// Negative ⇒ they overlap (value = −overlap depth, approximately); positive ⇒ the gap between
        /// the interval edge and the nearest arc edge.
        /// </summary>
        public static float ArcClearance(float center, float halfWidth, float arcStart, float arcWidth)
        {
            if (arcWidth >= TwoPi)
            {
                return -Pi;
            }

            float arcCenter = arcStart + arcWidth * 0.5f;
            float dist = AngleDistance(center, arcCenter);
            return dist - (arcWidth * 0.5f + halfWidth);
        }

        public static float Clamp(float v, float min, float max)
        {
            return v < min ? min : (v > max ? max : v);
        }

        public static float Clamp01(float v)
        {
            return v < 0f ? 0f : (v > 1f ? 1f : v);
        }

        public static int Clamp(int v, int min, int max)
        {
            return v < min ? min : (v > max ? max : v);
        }

        public static float Lerp(float a, float b, float t)
        {
            return a + (b - a) * Clamp01(t);
        }

        public static float InverseLerp(float a, float b, float v)
        {
            if (Math.Abs(b - a) < 1e-6f)
            {
                return 0f;
            }

            return Clamp01((v - a) / (b - a));
        }

        public static float SmoothStep(float t)
        {
            t = Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        /// <summary>Moves <paramref name="current"/> toward <paramref name="target"/> by at most maxDelta.</summary>
        public static float MoveTowards(float current, float target, float maxDelta)
        {
            if (Math.Abs(target - current) <= maxDelta)
            {
                return target;
            }

            return current + Math.Sign(target - current) * maxDelta;
        }

        /// <summary>Frame-rate independent exponential approach factor for a given sharpness (1/s).</summary>
        public static float Damp(float sharpness, float dt)
        {
            return 1f - (float)Math.Exp(-sharpness * dt);
        }
    }
}

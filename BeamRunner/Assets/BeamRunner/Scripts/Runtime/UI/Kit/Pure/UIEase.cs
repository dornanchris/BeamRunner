using System;

namespace BeamRunner.UI
{
    /// <summary>Easing curves for <c>UITween</c>.</summary>
    public enum UIEase
    {
        Linear = 0,
        InQuad,
        OutQuad,
        InOutQuad,
        InCubic,
        OutCubic,
        InOutCubic,
        InQuart,
        OutQuart,
        InOutQuart,
        InSine,
        OutSine,
        InOutSine,
        InBack,
        OutBack,
        InOutBack,
        InElastic,
        OutElastic,
        InOutElastic,
        OutBounce,
        /// <summary>Soft spring: quick rise, one gentle overshoot, settles (good for press release).</summary>
        OutSpring,
    }

    /// <summary>Engine-free easing functions (t in 0..1 → eased value; Back/Elastic/Spring overshoot).</summary>
    public static class UIEaseFunctions
    {
        private const float C1 = 1.70158f;
        private const float C2 = C1 * 1.525f;
        private const float C3 = C1 + 1f;
        private const float C4 = 2f * MathF.PI / 3f;
        private const float C5 = 2f * MathF.PI / 4.5f;

        public static float Evaluate(UIEase ease, float t)
        {
            if (t <= 0f)
            {
                return 0f;
            }

            if (t >= 1f)
            {
                return 1f;
            }

            switch (ease)
            {
                case UIEase.Linear:
                    return t;
                case UIEase.InQuad:
                    return t * t;
                case UIEase.OutQuad:
                    return 1f - (1f - t) * (1f - t);
                case UIEase.InOutQuad:
                    return t < 0.5f ? 2f * t * t : 1f - Pow2(-2f * t + 2f) / 2f;
                case UIEase.InCubic:
                    return t * t * t;
                case UIEase.OutCubic:
                    return 1f - Pow3(1f - t);
                case UIEase.InOutCubic:
                    return t < 0.5f ? 4f * t * t * t : 1f - Pow3(-2f * t + 2f) / 2f;
                case UIEase.InQuart:
                    return t * t * t * t;
                case UIEase.OutQuart:
                    return 1f - Pow2(Pow2(1f - t));
                case UIEase.InOutQuart:
                    return t < 0.5f ? 8f * t * t * t * t : 1f - Pow2(Pow2(-2f * t + 2f)) / 2f;
                case UIEase.InSine:
                    return 1f - MathF.Cos(t * MathF.PI / 2f);
                case UIEase.OutSine:
                    return MathF.Sin(t * MathF.PI / 2f);
                case UIEase.InOutSine:
                    return -(MathF.Cos(MathF.PI * t) - 1f) / 2f;
                case UIEase.InBack:
                    return C3 * t * t * t - C1 * t * t;
                case UIEase.OutBack:
                    return 1f + C3 * Pow3(t - 1f) + C1 * Pow2(t - 1f);
                case UIEase.InOutBack:
                    return t < 0.5f
                        ? Pow2(2f * t) * ((C2 + 1f) * 2f * t - C2) / 2f
                        : (Pow2(2f * t - 2f) * ((C2 + 1f) * (t * 2f - 2f) + C2) + 2f) / 2f;
                case UIEase.InElastic:
                    return -MathF.Pow(2f, 10f * t - 10f) * MathF.Sin((t * 10f - 10.75f) * C4);
                case UIEase.OutElastic:
                    return MathF.Pow(2f, -10f * t) * MathF.Sin((t * 10f - 0.75f) * C4) + 1f;
                case UIEase.InOutElastic:
                    return t < 0.5f
                        ? -(MathF.Pow(2f, 20f * t - 10f) * MathF.Sin((20f * t - 11.125f) * C5)) / 2f
                        : MathF.Pow(2f, -20f * t + 10f) * MathF.Sin((20f * t - 11.125f) * C5) / 2f + 1f;
                case UIEase.OutBounce:
                    return OutBounce(t);
                case UIEase.OutSpring:
                    // Damped cosine: ~12% overshoot, settles by t = 1.
                    return 1f - MathF.Exp(-6.5f * t) * MathF.Cos(t * 9.5f) * (1f - t * t * t);
                default:
                    return t;
            }
        }

        /// <summary>Punch curve: 0 → +1 (peak) → small negative → 0 over t = 0..1 (for scale punches).</summary>
        public static float Punch(float t)
        {
            if (t <= 0f || t >= 1f)
            {
                return 0f;
            }

            float decay = (1f - t) * (1f - t);
            // 1.49 normalises the first peak to ~1.
            return MathF.Sin(t * MathF.PI * 2.5f) * decay * 1.49f;
        }

        private static float OutBounce(float t)
        {
            const float n1 = 7.5625f;
            const float d1 = 2.75f;
            if (t < 1f / d1)
            {
                return n1 * t * t;
            }

            if (t < 2f / d1)
            {
                t -= 1.5f / d1;
                return n1 * t * t + 0.75f;
            }

            if (t < 2.5f / d1)
            {
                t -= 2.25f / d1;
                return n1 * t * t + 0.9375f;
            }

            t -= 2.625f / d1;
            return n1 * t * t + 0.984375f;
        }

        private static float Pow2(float x)
        {
            return x * x;
        }

        private static float Pow3(float x)
        {
            return x * x * x;
        }
    }
}

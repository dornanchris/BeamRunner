using System;
using System.Collections.Generic;
using BeamRunner.Core.Sim;

namespace BeamRunner.Core.Balance
{
    /// <summary>
    /// What the hidden skill rating changes in a run (1 = Standard; the design doc's table). Applied at run start and at
    /// each level wall checkpoint, never mid-chunk. Edge rules (ceiling, void, two-layer falls) never change.
    /// </summary>
    [Serializable]
    public sealed class DifficultyModifiers
    {
        public float Rating;
        public float Speed = 1f;

        /// <summary>Gaps and layer changes (density of chunks).</summary>
        public float Gaps = 1f;

        /// <summary>Fuel pickups.</summary>
        public float Fuel = 1f;

        /// <summary>Obstacles (level 5+).</summary>
        public float Obstacles = 1f;

        /// <summary>Shield and repair drops.</summary>
        public float Drops = 1f;

        /// <summary>Lead time of the gap warning on the ghost outline.</summary>
        public float GhostLead = 1f;

        /// <summary>Gem payout.</summary>
        public float Gems = 1f;

        public static DifficultyModifiers Standard
        {
            get { return new DifficultyModifiers(); }
        }

        public DifficultyModifiers Clone()
        {
            return (DifficultyModifiers)MemberwiseClone();
        }
    }

    /// <summary>The pilot's hidden skill rating and the history that moves it (saved in the profile).</summary>
    [Serializable]
    public sealed class SkillState
    {
        /// <summary>−2 (Relaxed) … +2 (Expert); every pilot starts at 0 (Standard).</summary>
        public float Rating;

        /// <summary>Performance (−1..1) of the most recent runs, oldest first (at most the rating window).</summary>
        public List<float> Recent = new List<float>();

        /// <summary>Consecutive runs that ended within the early-death time.</summary>
        public int EarlyDeaths;

        public void Sanitize()
        {
            if (float.IsNaN(Rating) || float.IsInfinity(Rating))
            {
                Rating = 0f;
            }

            Rating = MathUtil.Clamp(Rating, AdaptiveDifficulty.MinRating, AdaptiveDifficulty.MaxRating);
            Recent = Recent ?? new List<float>();
            for (int i = Recent.Count - 1; i >= 0; i--)
            {
                if (float.IsNaN(Recent[i]) || float.IsInfinity(Recent[i]))
                {
                    Recent.RemoveAt(i);
                }
            }

            EarlyDeaths = Math.Max(0, EarlyDeaths);
        }
    }

    /// <summary>
    /// The adaptive difficulty of the design doc: a hidden rating from −2 to +2 tunes each run on top of the normal level
    /// scaling. After each run its result is compared with the result expected for the pilot's rating, upgrades and level
    /// (<see cref="Performance"/>: depth, falls, damage and wasted fuel); the rating moves by the step × the rolling average
    /// of the last runs, clamped, and two early deaths in a row drop it an extra notch (<see cref="ApplyRun"/>).
    /// Pure and deterministic.
    /// </summary>
    public static class AdaptiveDifficulty
    {
        public const float MinRating = -2f;
        public const float MaxRating = 2f;

        /// <summary>Modifiers for a rating, interpolated between the five table steps.</summary>
        public static DifficultyModifiers For(float rating, BalanceConfig b = null)
        {
            b = b ?? BalanceConfig.Current;
            rating = MathUtil.Clamp(float.IsNaN(rating) ? 0f : rating, MinRating, MaxRating);
            return new DifficultyModifiers
            {
                Rating = rating,
                Speed = Sample(b.RatingSpeed, rating),
                Gaps = Sample(b.RatingGaps, rating),
                Fuel = Sample(b.RatingFuel, rating),
                Obstacles = Sample(b.RatingObstacles, rating),
                Drops = Sample(b.RatingDrops, rating),
                GhostLead = Sample(b.RatingGhostLead, rating),
                Gems = Sample(b.RatingGems, rating),
            };
        }

        /// <summary>Label key of the nearest step ("difficulty.relaxed" … "difficulty.expert"), for debug and a "Heat" meter.</summary>
        public static string LabelKey(float rating)
        {
            int step = (int)Math.Round(MathUtil.Clamp(rating, MinRating, MaxRating));
            switch (step)
            {
                case -2:
                    return "difficulty.relaxed";
                case -1:
                    return "difficulty.easy";
                case 1:
                    return "difficulty.hard";
                case 2:
                    return "difficulty.expert";
                default:
                    return "difficulty.standard";
            }
        }

        /// <summary>Level a pilot with this craft is expected to reach (at least 1).</summary>
        public static float ExpectedLevel(ShipStats ship, BalanceConfig b = null)
        {
            b = b ?? BalanceConfig.Current;
            int power = ship != null ? ship.Power : 0;
            return Math.Max(1f, b.ExpectedLevelBase + b.ExpectedLevelPerUpgrade * power);
        }

        /// <summary>
        /// How far a run beat (+) or missed (−) the expected result, −1..1: mostly how deep it got compared with
        /// <see cref="ExpectedLevel"/>, minus the damage taken, a revive and the fuel wasted on tiles that already had rail.
        /// </summary>
        public static float Performance(RunResult r, ShipStats ship, BalanceConfig b = null)
        {
            if (r == null)
            {
                return 0f;
            }

            b = b ?? BalanceConfig.Current;
            float expected = Math.Max(ExpectedLevel(ship, b), r.StartLevel + 0.5f);
            float reached = r.LevelReached + MathUtil.Clamp01(r.LevelProgress);
            float depth = MathUtil.Clamp((reached - expected) / 2f, -1f, 1f);
            float hits = 0.12f * r.DamageTaken + (r.Revived ? 0.3f : 0f);
            float waste = r.FuelSpent > 0f ? MathUtil.Clamp01(r.FuelWasted / r.FuelSpent) : 0f;
            return MathUtil.Clamp(0.85f * depth + 0.1f - hits - 0.3f * waste, -1f, 1f);
        }

        /// <summary>
        /// Updates the rating after a run: R = clamp(R + step × mean(P of the last runs), −2, 2), and an extra drop of
        /// <see cref="BalanceConfig.RagePenalty"/> on the second early death in a row. Returns the change.
        /// </summary>
        public static float ApplyRun(SkillState state, float performance, float runSeconds, BalanceConfig b = null)
        {
            if (state == null)
            {
                return 0f;
            }

            b = b ?? BalanceConfig.Current;
            state.Sanitize();
            float before = state.Rating;
            state.Recent.Add(MathUtil.Clamp(float.IsNaN(performance) ? 0f : performance, -1f, 1f));
            while (state.Recent.Count > b.RatingWindow)
            {
                state.Recent.RemoveAt(0);
            }

            float sum = 0f;
            for (int i = 0; i < state.Recent.Count; i++)
            {
                sum += state.Recent[i];
            }

            float mean = state.Recent.Count > 0 ? sum / state.Recent.Count : 0f;
            float rating = state.Rating + b.RatingStep * mean;
            if (runSeconds < b.EarlyDeathSeconds)
            {
                state.EarlyDeaths++;
                if (state.EarlyDeaths >= 2)
                {
                    rating -= b.RagePenalty;
                    state.EarlyDeaths = 0;
                }
            }
            else
            {
                state.EarlyDeaths = 0;
            }

            state.Rating = MathUtil.Clamp(rating, MinRating, MaxRating);
            return state.Rating - before;
        }

        private static float Sample(float[] table, float rating)
        {
            if (table == null || table.Length == 0)
            {
                return 1f;
            }

            float f = (rating - MinRating) / (MaxRating - MinRating) * (table.Length - 1);
            int i = MathUtil.Clamp((int)Math.Floor(f), 0, table.Length - 1);
            int j = Math.Min(i + 1, table.Length - 1);
            return MathUtil.Lerp(table[i], table[j], MathUtil.Clamp01(f - i));
        }
    }
}

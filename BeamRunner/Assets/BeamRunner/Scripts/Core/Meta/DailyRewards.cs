using System;
using System.Globalization;

namespace BeamRunner.Core.Meta
{
    /// <summary>Persisted daily-login streak.</summary>
    [Serializable]
    public sealed class DailyRewardState
    {
        /// <summary>Local calendar date of the last claim, "yyyy-MM-dd" (empty = never).</summary>
        public string LastClaimDate = string.Empty;

        /// <summary>Consecutive days claimed in the current streak (1..7 after a claim).</summary>
        public int Streak;
    }

    /// <summary>
    /// Seven-day login calendar. Claiming on consecutive local calendar days advances the streak; missing a
    /// day restarts it at day 1; after day 7 it wraps back to day 1. Uses the device's local date — good enough
    /// for a single-player game (clock changes only affect the player's own rewards).
    /// </summary>
    public static class DailyRewards
    {
        public static readonly int[] Rewards = { 50, 75, 100, 150, 200, 300, 600 };

        public static string DateKey(DateTime localDate)
        {
            return localDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        public static bool CanClaim(DailyRewardState state, DateTime localNow)
        {
            return state.LastClaimDate != DateKey(localNow);
        }

        /// <summary>0-based day index (0..6) that the next claim would pay out.</summary>
        public static int NextDayIndex(DailyRewardState state, DateTime localNow)
        {
            if (string.IsNullOrEmpty(state.LastClaimDate))
            {
                return 0;
            }

            if (state.LastClaimDate == DateKey(localNow))
            {
                // Already claimed today: show today's slot.
                return MathUtil.Clamp(state.Streak - 1, 0, Rewards.Length - 1);
            }

            bool consecutive = state.LastClaimDate == DateKey(localNow.Date.AddDays(-1));
            if (!consecutive || state.Streak >= Rewards.Length)
            {
                return 0;
            }

            return MathUtil.Clamp(state.Streak, 0, Rewards.Length - 1);
        }

        /// <summary>Claims today's reward. Returns the amount, or 0 if already claimed today.</summary>
        public static int Claim(DailyRewardState state, DateTime localNow)
        {
            if (!CanClaim(state, localNow))
            {
                return 0;
            }

            int day = NextDayIndex(state, localNow);
            state.Streak = day + 1;
            state.LastClaimDate = DateKey(localNow);
            return Rewards[day];
        }
    }
}

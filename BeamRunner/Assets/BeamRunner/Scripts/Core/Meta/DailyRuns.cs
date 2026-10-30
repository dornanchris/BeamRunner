using System;
using System.Globalization;

namespace BeamRunner.Core.Meta
{
    /// <summary>
    /// Rules of the Daily Run: one track per UTC day, identical for every player, unlimited attempts, a per-day best,
    /// a streak of consecutive UTC days and modest gem rewards. Pure functions; the persisted state is
    /// <see cref="DailyRunState"/> and <see cref="ProfileService"/> applies finished runs.
    /// <para>
    /// <b>Why UTC.</b> The day (and so the seed) is the UTC calendar date, not the device's local date: every player
    /// worldwide flies the same track at the same moment (one global leaderboard window per day), the track changes at
    /// one instant for everybody (00:00 UTC), and travelling across time zones or a daylight-saving switch never moves
    /// the day — so it can neither break a streak nor grant a second day's reward. Only changing the device clock
    /// itself moves the UTC day; see <see cref="ProfileService.ApplyDailyRun"/> for how a clock that goes backwards
    /// is handled (never rewarded).
    /// </para>
    /// <para>
    /// <b>Determinism.</b> The seed is derived from the day number with integer arithmetic only (a bijective 32-bit
    /// mix), so it is bit-identical on every platform and build, and different days always get different seeds.
    /// The track itself comes from <see cref="Field.TrackGenerator"/> with that seed, no tutorial, from level 1, at the
    /// Standard rating.
    /// Players only share a track while they run the same generator: bump <see cref="GeneratorVersion"/> whenever
    /// a change alters generated tracks (pin a golden fingerprint in <c>DailyRunTests</c> once the tuning settles).
    /// </para>
    /// </summary>
    public static class DailyRuns
    {
        /// <summary>
        /// Version of the track generator/simulation the daily tracks come from. Stored in every ghost (a ghost of an
        /// older version is never shown) and useful to keep online daily leaderboards per version.
        /// </summary>
        /// <remarks>
        /// 1: initial release. 2: every gate opening at least 2 × 2 cells, gates further apart. 3: the Spiral level (4)
        /// and the levels after it renumbered. 4: gems from level 3 at half the rate, power-ups on their own schedule.
        /// 5: Spiral walls block three quarters from a random side, deadly edges in Drift levels, denser gem trails.
        /// 6: shorter early levels and level gaps, Spiral walls closer together, gliding holes, half the gem trails
        /// through cubes, cubes along the edges of early cube fields, retuned Crossfire / Shifting Needle / Gauntlet.
        /// BeamRunner restarts the count: 1 = the 3-lane chunk generator.
        /// </remarks>
        public const int GeneratorVersion = 1;

        /// <summary>
        /// No continue after a crash in the Daily Run. Fairness: it is the one mode whose score is compared between
        /// players, and a revive is bought with gems (which can be bought with money) or depends on a rewarded ad
        /// being available (region, network, fill). Attempts are unlimited, so a crash only costs a retry.
        /// </summary>
        public const int MaxRevives = 0;

        /// <summary>
        /// No pilot-rank score bonus in the Daily Run (normal runs get +4 % per rank): the daily score depends on the
        /// flying only, so it is comparable between a new pilot and a veteran.
        /// </summary>
        public const float ScoreBonus = 0f;

        /// <summary>A run shorter than this (an instant quit/restart) does not count for bests, streak or rewards.</summary>
        public const float MinCountedDistance = 100f;

        /// <summary>Gems for the first counted Daily Run of a UTC day (= day 1 of the login calendar).</summary>
        public const int FirstRunReward = 50;

        /// <summary>
        /// A recorded day this many days AHEAD of the device clock is treated as a broken clock record (e.g. a device
        /// that once booted in 2037): the streak restarts and the record is re-based to the current day, still
        /// without a reward for that day. Smaller rollbacks keep the record and simply play unrecorded.
        /// </summary>
        public const int MaxClockSkewDays = 30;

        /// <summary>Streak lengths (consecutive UTC days) that pay <see cref="StreakMilestoneRewards"/>.</summary>
        public static readonly int[] StreakMilestones = { 3, 7, 14, 30 };

        /// <summary>Gems paid when the streak reaches the matching <see cref="StreakMilestones"/> entry.</summary>
        public static readonly int[] StreakMilestoneRewards = { 100, 250, 400, 800 };

        /// <summary>After the last milestone, every further multiple of this streak length pays the last reward again.</summary>
        public const int StreakRepeatEvery = 30;

        /// <summary>First plausible day (2024-01-01): an earlier device clock is wrong, so nothing is recorded.</summary>
        public static readonly int MinValidDay = DayIndex(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        /// <summary>Last plausible day (2200-01-01).</summary>
        public static readonly int MaxValidDay = DayIndex(new DateTime(2200, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        private const long EpochTicks = 621355968000000000L; // 1970-01-01T00:00:00Z
        private const uint SeedSalt = 0xD417A5EDu;

        // ------------------------------------------------------------------ calendar

        /// <summary>
        /// Interprets a clock reading as UTC: <see cref="DateTimeKind.Local"/> is converted, <see cref="DateTimeKind.Utc"/>
        /// and <see cref="DateTimeKind.Unspecified"/> are taken as UTC (callers pass <c>DateTime.UtcNow</c>).
        /// </summary>
        public static DateTime ToUtc(DateTime time)
        {
            if (time.Kind == DateTimeKind.Local)
            {
                return time.ToUniversalTime();
            }

            return time.Kind == DateTimeKind.Utc ? time : DateTime.SpecifyKind(time, DateTimeKind.Utc);
        }

        /// <summary>UTC day number: whole days since 1970-01-01T00:00Z (floor, so it is exact at midnight).</summary>
        public static int DayIndex(DateTime time)
        {
            long ticks = ToUtc(time).Ticks - EpochTicks;
            long day = ticks >= 0 ? ticks / TimeSpan.TicksPerDay : -((-ticks + TimeSpan.TicksPerDay - 1) / TimeSpan.TicksPerDay);
            return (int)day;
        }

        /// <summary>UTC day number of an instant given with any UTC offset (the offset does not matter).</summary>
        public static int DayIndex(DateTimeOffset time)
        {
            return DayIndex(time.UtcDateTime);
        }

        /// <summary>True for day numbers a correct device clock can produce (2024 … 2200).</summary>
        public static bool IsValidDay(int day)
        {
            return day >= MinValidDay && day <= MaxValidDay;
        }

        /// <summary>00:00 UTC of <paramref name="day"/> (clamped to the range <see cref="DateTime"/> can represent).</summary>
        public static DateTime DayStartUtc(int day)
        {
            long minDay = -(EpochTicks / TimeSpan.TicksPerDay);
            long maxDay = (DateTime.MaxValue.Ticks - EpochTicks) / TimeSpan.TicksPerDay;
            long d = Math.Max(minDay, Math.Min(maxDay, (long)day));
            return new DateTime(EpochTicks + d * TimeSpan.TicksPerDay, DateTimeKind.Utc);
        }

        /// <summary>When the next track goes live: the next 00:00 UTC after <paramref name="utcNow"/>.</summary>
        public static DateTime NextResetUtc(DateTime utcNow)
        {
            return DayStartUtc(DayIndex(utcNow) + 1);
        }

        /// <summary>Time left until <see cref="NextResetUtc"/> (always &gt; 0 and ≤ 24 h).</summary>
        public static TimeSpan TimeUntilReset(DateTime utcNow)
        {
            return NextResetUtc(utcNow) - ToUtc(utcNow);
        }

        /// <summary>"yyyy-MM-dd" of a UTC day (leaderboard keys, logs).</summary>
        public static string DayKey(int day)
        {
            return DayStartUtc(day).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        // ------------------------------------------------------------------ track

        /// <summary>
        /// Track seed of a UTC day. Integer-only bijective mix of the day number (xor salt, odd multiply, murmur3
        /// finaliser), so it is identical everywhere and two different days never share a seed.
        /// </summary>
        public static int SeedForDay(int day)
        {
            unchecked
            {
                uint x = (uint)day ^ SeedSalt;
                x *= 0x9E3779B1u;
                x ^= x >> 16;
                x *= 0x85EBCA6Bu;
                x ^= x >> 13;
                x *= 0xC2B2AE35u;
                x ^= x >> 16;
                return (int)x;
            }
        }

        // ------------------------------------------------------------------ streak & rewards

        /// <summary>
        /// Streak to show on <paramref name="today"/>: the stored streak while it is still alive (last counted run today or
        /// yesterday, or a clock that is behind the record), else 0 (a day was missed; the next counted run restarts at 1).
        /// </summary>
        public static int CurrentStreak(DailyRunState state, int today)
        {
            if (state == null || state.Day <= 0 || state.Streak <= 0)
            {
                return 0;
            }

            return today <= state.Day + 1 ? state.Streak : 0;
        }

        /// <summary>True when the streak survives only if a Daily Run is counted today (played yesterday, not yet today).</summary>
        public static bool StreakAtRisk(DailyRunState state, int today)
        {
            return state != null && state.Streak > 0 && state.Day > 0 && today == state.Day + 1;
        }

        /// <summary>Gems paid when the streak reaches <paramref name="streak"/> (0 if it is not a milestone).</summary>
        public static int StreakMilestoneReward(int streak)
        {
            for (int i = 0; i < StreakMilestones.Length; i++)
            {
                if (streak == StreakMilestones[i])
                {
                    return StreakMilestoneRewards[i];
                }
            }

            int last = StreakMilestones[StreakMilestones.Length - 1];
            if (streak > last && streak % StreakRepeatEvery == 0)
            {
                return StreakMilestoneRewards[StreakMilestoneRewards.Length - 1];
            }

            return 0;
        }

        /// <summary>The next streak length that pays a reward, strictly after <paramref name="streak"/>.</summary>
        public static int NextStreakMilestone(int streak)
        {
            for (int i = 0; i < StreakMilestones.Length; i++)
            {
                if (StreakMilestones[i] > streak)
                {
                    return StreakMilestones[i];
                }
            }

            return (Math.Max(streak, 0) / StreakRepeatEvery + 1) * StreakRepeatEvery;
        }
    }
}

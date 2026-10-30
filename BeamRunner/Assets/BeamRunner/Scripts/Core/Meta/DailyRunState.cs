using System;
using BeamRunner.Core.Sim;

namespace BeamRunner.Core.Meta
{
    /// <summary>
    /// Persisted Daily Run progress (<see cref="PlayerProfile.DailyRun"/>). JsonUtility-compatible
    /// public fields; every field's default (0 / empty) means "nothing recorded", so a save written before the Daily
    /// Run existed loads as a fresh state. Repaired by <see cref="Sanitize"/>.
    /// </summary>
    [Serializable]
    public sealed class DailyRunState
    {
        /// <summary>
        /// UTC day number (<see cref="DailyRuns.DayIndex(DateTime)"/>) of the latest counted Daily Run; 0 = never.
        /// The best/attempts/ghost fields below belong to this day. It is also the high-water mark of the clock: no
        /// reward or streak change is ever granted for a day at or before it.
        /// </summary>
        public int Day;

        /// <summary>Consecutive UTC days, ending at <see cref="Day"/>, with at least one counted Daily Run.</summary>
        public int Streak;

        /// <summary>Longest streak ever.</summary>
        public int BestStreak;

        /// <summary>Best score on <see cref="Day"/>.</summary>
        public long BestScore;

        /// <summary>Distance of that best run (metres).</summary>
        public float BestDistance;

        /// <summary>Ship flown in that best run (the ghost's look).</summary>
        public string BestShip = string.Empty;

        /// <summary>Counted attempts on <see cref="Day"/>.</summary>
        public int Attempts;

        /// <summary>The best run of <see cref="Day"/> as an encoded <see cref="GhostPath"/> ("" = none).</summary>
        public string Ghost = string.Empty;

        /// <summary>Distinct UTC days with a counted Daily Run.</summary>
        public int DaysPlayed;

        /// <summary>Counted Daily Runs, all days.</summary>
        public int TotalRuns;

        /// <summary>Best Daily Run score on any day.</summary>
        public long AllTimeBestScore;

        /// <summary>
        /// Merges two copies of the same player's Daily Run state (cloud save, two devices) into a new state without
        /// losing progress or re-opening rewards: the later <see cref="Day"/> wins (its best, ghost, attempts and streak;
        /// on the same day the higher best score and the longer streak), lifetime counters take the maximum. The result's
        /// <see cref="Day"/> is the later high-water mark, so no day can be rewarded twice. Null-safe.
        /// </summary>
        public static DailyRunState Merge(DailyRunState a, DailyRunState b)
        {
            if (a == null || b == null)
            {
                DailyRunState only = a ?? b ?? new DailyRunState();
                return Copy(only);
            }

            DailyRunState later = a.Day >= b.Day ? a : b;
            DailyRunState other = ReferenceEquals(later, a) ? b : a;
            DailyRunState m = Copy(later);
            if (other.Day == later.Day)
            {
                m.Streak = Math.Max(a.Streak, b.Streak);
                m.Attempts = Math.Max(a.Attempts, b.Attempts);
                if (other.BestScore > later.BestScore)
                {
                    m.BestScore = other.BestScore;
                    m.BestDistance = other.BestDistance;
                    m.BestShip = other.BestShip;
                    m.Ghost = other.Ghost;
                }
            }

            m.BestStreak = Math.Max(a.BestStreak, b.BestStreak);
            m.DaysPlayed = Math.Max(a.DaysPlayed, b.DaysPlayed);
            m.TotalRuns = Math.Max(a.TotalRuns, b.TotalRuns);
            m.AllTimeBestScore = Math.Max(a.AllTimeBestScore, b.AllTimeBestScore);
            m.Sanitize();
            return m;
        }

        private static DailyRunState Copy(DailyRunState s)
        {
            return new DailyRunState
            {
                Day = s.Day,
                Streak = s.Streak,
                BestStreak = s.BestStreak,
                BestScore = s.BestScore,
                BestDistance = s.BestDistance,
                BestShip = s.BestShip,
                Attempts = s.Attempts,
                Ghost = s.Ghost,
                DaysPlayed = s.DaysPlayed,
                TotalRuns = s.TotalRuns,
                AllTimeBestScore = s.AllTimeBestScore,
            };
        }

        /// <summary>Repairs out-of-range values from corrupt or tampered saves. Idempotent.</summary>
        public void Sanitize()
        {
            if (Day != 0 && !DailyRuns.IsValidDay(Day))
            {
                // An impossible date: forget the day's record (it can never match a real day's track).
                Day = 0;
                Streak = 0;
                BestScore = 0;
                BestDistance = 0f;
                Attempts = 0;
                Ghost = string.Empty;
            }

            Streak = MathUtil.Clamp(Streak, 0, 100000);
            if (Day == 0)
            {
                Streak = 0;
            }
            else if (Streak == 0)
            {
                Streak = 1; // a recorded day is at least a one-day streak
            }

            BestStreak = MathUtil.Clamp(Math.Max(BestStreak, Streak), 0, 100000);
            if (BestScore < 0)
            {
                BestScore = 0;
            }

            if (float.IsNaN(BestDistance) || BestDistance < 0f)
            {
                BestDistance = 0f;
            }

            BestShip = BestShip ?? string.Empty;
            Ghost = Ghost ?? string.Empty;
            if (Ghost.Length > GhostPath.MaxEncodedChars)
            {
                Ghost = string.Empty;
            }

            Attempts = Math.Max(0, Attempts);
            DaysPlayed = Math.Max(0, DaysPlayed);
            TotalRuns = Math.Max(0, TotalRuns);
            AllTimeBestScore = Math.Max(Math.Max(0L, AllTimeBestScore), BestScore);
        }
    }

    /// <summary>
    /// Context of one Daily Run, captured when it starts (<see cref="ProfileService.BeginDailyRun"/>): the UTC day and
    /// seed of the track actually flown (a run that crosses midnight still belongs to the day it started on), the
    /// ghost to race, and whether the clock allows recording it. Apply it once with
    /// <see cref="ProfileService.ApplyDailyRun"/>.
    /// </summary>
    public sealed class DailyRunTicket
    {
        /// <summary>UTC day of the track.</summary>
        public int Day;

        /// <summary><see cref="DailyRuns.SeedForDay"/> of <see cref="Day"/>.</summary>
        public int Seed;

        /// <summary>Today's best run to race (null: none yet, or ghosts are off).</summary>
        public GhostPath Ghost;

        /// <summary>
        /// The device clock is behind the recorded high-water day (or implausible): the run plays normally but is not
        /// recorded — no best, ghost, streak, reward or leaderboard event.
        /// </summary>
        public bool Unrecorded;

        /// <summary>Set once the run was applied (a ticket is never applied twice).</summary>
        public bool Applied;

        /// <summary>The outcome of the application (null until applied).</summary>
        public DailyRunOutcome Outcome;
    }

    /// <summary>What a finished Daily Run changed — drives the results screen and the <c>DailyRunCompleted</c> hook.</summary>
    public sealed class DailyRunOutcome
    {
        /// <summary>UTC day of the track flown.</summary>
        public int Day;

        /// <summary>"yyyy-MM-dd" of <see cref="Day"/> (UTC).</summary>
        public string DayKey = string.Empty;

        /// <summary>Track seed.</summary>
        public int Seed;

        /// <summary><see cref="DailyRuns.GeneratorVersion"/> of the build that flew it.</summary>
        public int GeneratorVersion = DailyRuns.GeneratorVersion;

        public long Score;
        public float Distance;

        /// <summary>
        /// The run was recorded (long enough, plausible clock). Only counted runs update bests, streak and rewards and
        /// are announced through <c>ProfileService.DailyRunCompleted</c>.
        /// </summary>
        public bool Counted;

        /// <summary>Not counted because the device clock went backwards / is implausible.</summary>
        public bool ClockRejected;

        /// <summary>Not counted because it ended before <see cref="DailyRuns.MinCountedDistance"/>.</summary>
        public bool TooShort;

        /// <summary>First counted run of this UTC day.</summary>
        public bool FirstRunToday;

        /// <summary>Beat the day's previous best (always true for the first counted run of a day with a score).</summary>
        public bool NewDailyBest;

        /// <summary>The day's best before this run (0 on the first run).</summary>
        public long PreviousDailyBest;

        /// <summary>The day's best after this run.</summary>
        public long DailyBestScore;

        public float DailyBestDistance;

        /// <summary>Counted attempts today, including this one.</summary>
        public int Attempts;

        /// <summary>Streak after this run (0 when the run was not counted and no streak is alive).</summary>
        public int Streak;

        /// <summary>This run extended (or started) the streak.</summary>
        public bool StreakExtended;

        /// <summary>Streak milestone reached by this run (0 = none).</summary>
        public int StreakMilestone;

        /// <summary>Gems for the first counted run of the day.</summary>
        public int FirstRunReward;

        /// <summary>Gems for the streak milestone.</summary>
        public int StreakReward;

        /// <summary>A new ghost (this run) was stored for the day.</summary>
        public bool GhostRecorded;

        /// <summary>The run was a new all-time best Daily Run score.</summary>
        public bool NewAllTimeBest;

        /// <summary>Total gems granted for this Daily Run on top of the normal run's gems.</summary>
        public int TotalReward
        {
            get { return FirstRunReward + StreakReward; }
        }
    }
}

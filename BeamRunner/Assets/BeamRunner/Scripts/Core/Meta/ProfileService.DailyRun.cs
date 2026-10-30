using System;
using System.Collections.Generic;
using BeamRunner.Core.Sim;

namespace BeamRunner.Core.Meta
{
    /// <summary>
    /// Daily Run part of the profile rules (see <see cref="DailyRuns"/> for the calendar/seed/reward tables and
    /// <see cref="DailyRunState"/> for what is saved). Kept in its own file; the only hooks elsewhere are
    /// <see cref="RunSummary.Daily"/> and <see cref="PlayerProfile.DailyRun"/>.
    /// <para><b>Recording rules</b> (<see cref="ApplyDailyRun"/>), with D = the UTC day the run STARTED on and
    /// H = <see cref="DailyRunState.Day"/>, the latest recorded day (the clock's high-water mark):</para>
    /// <list type="bullet">
    /// <item>A run counts when it ends (crash, quit or restart) after at least <see cref="DailyRuns.MinCountedDistance"/>
    /// metres. Shorter runs change nothing.</item>
    /// <item>D &gt; H — first counted run of a new day: the day's best/ghost/attempts restart, the streak becomes
    /// H + 1 == D ? streak + 1 : 1 (a missed UTC day restarts it), and it pays <see cref="DailyRuns.FirstRunReward"/>
    /// plus the streak milestone reward if the new streak is a milestone. Each day can pass this branch only once, so
    /// every reward is paid at most once.</item>
    /// <item>D == H — another attempt the same day: attempts + 1, best and ghost replaced if the score is higher. No
    /// reward, no streak change.</item>
    /// <item>D &lt; H — the device clock went backwards: the run is not recorded at all (no best, ghost, streak, reward
    /// or <see cref="DailyRunCompleted"/>). If H is more than <see cref="DailyRuns.MaxClockSkewDays"/> days ahead, the
    /// record itself is treated as the broken clock reading: it is re-based to D with a streak of 1 and still no reward
    /// for D, so a player whose device once had a wrong date is not locked out for months.</item>
    /// <item>An implausible clock (before 2024 / after 2200) never records.</item>
    /// </list>
    /// Time zones and daylight saving cannot move D (it is UTC). A run crossing midnight belongs to the day it started
    /// on (its track). Setting the clock FORWARD can't be detected offline — as for the login calendar, that only
    /// affects the player's own single-player rewards; online leaderboards should check the day server-side.
    /// </summary>
    public sealed partial class ProfileService
    {
        private static readonly Func<DateTime> SystemUtcClock = () => DateTime.UtcNow;

        private Func<DateTime> _utcClock;

        /// <summary>UTC clock used by the Daily Run (defaults to <see cref="DateTime.UtcNow"/>; tests replace it).</summary>
        public Func<DateTime> UtcClock
        {
            get { return _utcClock ?? SystemUtcClock; }
            set { _utcClock = value; }
        }

        /// <summary>
        /// Raised once per COUNTED Daily Run, after the profile was updated (bests, streak, rewards) and before the
        /// save. Leaderboard/achievement hook: <see cref="DailyRunOutcome.Score"/>, <see cref="DailyRunOutcome.DayKey"/>,
        /// <see cref="DailyRunOutcome.Seed"/>, <see cref="DailyRunOutcome.GeneratorVersion"/>. Never raised for runs the
        /// clock rules rejected or that were too short.
        /// </summary>
        public event Action<DailyRunOutcome> DailyRunCompleted;

        // ------------------------------------------------------------------ read-only state (UI, notifications, services)

        /// <summary>Now, in UTC, from <see cref="UtcClock"/>.</summary>
        public DateTime UtcNow
        {
            get { return DailyRuns.ToUtc(UtcClock()); }
        }

        /// <summary>Today's UTC day number (<see cref="DailyRuns.DayIndex(DateTime)"/>).</summary>
        public int DailyRunToday
        {
            get { return DailyRuns.DayIndex(UtcNow); }
        }

        /// <summary>The Daily Run opens once the first flight (tutorial) is done.</summary>
        public bool DailyRunUnlocked
        {
            get { return Profile.Flags != null && Profile.Flags.TutorialDone; }
        }

        /// <summary>True when a Daily Run was counted today (UTC).</summary>
        public bool DailyRunPlayedToday
        {
            get
            {
                DailyRunState s = DailyState;
                return s.Day > 0 && s.Day == DailyRunToday;
            }
        }

        /// <summary>Current streak (0 once a UTC day was missed; see <see cref="DailyRuns.CurrentStreak"/>).</summary>
        public int DailyRunStreak
        {
            get { return DailyRuns.CurrentStreak(DailyState, DailyRunToday); }
        }

        /// <summary>Played yesterday but not yet today: the streak is lost at <see cref="DailyRunNextResetUtc"/>.</summary>
        public bool DailyRunStreakAtRisk
        {
            get { return DailyRuns.StreakAtRisk(DailyState, DailyRunToday); }
        }

        /// <summary>When today's track is replaced (next 00:00 UTC).</summary>
        public DateTime DailyRunNextResetUtc
        {
            get { return DailyRuns.NextResetUtc(UtcNow); }
        }

        /// <summary>Time left on today's track.</summary>
        public TimeSpan DailyRunTimeUntilReset
        {
            get { return DailyRuns.TimeUntilReset(UtcNow); }
        }

        /// <summary>Today's best Daily Run score (0 if not played today).</summary>
        public long DailyRunBestScoreToday
        {
            get { return DailyRunPlayedToday ? DailyState.BestScore : 0; }
        }

        /// <summary>Distance of today's best Daily Run (0 if not played today).</summary>
        public float DailyRunBestDistanceToday
        {
            get { return DailyRunPlayedToday ? DailyState.BestDistance : 0f; }
        }

        /// <summary>Counted attempts today.</summary>
        public int DailyRunAttemptsToday
        {
            get { return DailyRunPlayedToday ? DailyState.Attempts : 0; }
        }

        /// <summary>
        /// False while a Daily Run started now would not be recorded because the device clock is behind the last
        /// recorded day (or implausible). The run is still playable.
        /// </summary>
        public bool DailyRunRecordable
        {
            get { return IsRecordable(DailyState, DailyRunToday); }
        }

        /// <summary>The ghost of <paramref name="day"/>'s best run, or null (none, other day, other track/version, damaged).</summary>
        public GhostPath DailyRunGhost(int day)
        {
            DailyRunState s = DailyState;
            if (s.Day <= 0 || s.Day != day || string.IsNullOrEmpty(s.Ghost))
            {
                return null;
            }

            GhostPath ghost = GhostPath.Decode(s.Ghost);
            return IsGhostFor(ghost, DailyRuns.SeedForDay(day)) ? ghost : null;
        }

        // ------------------------------------------------------------------ run lifecycle

        /// <summary>
        /// Captures today's Daily Run context — call when the run starts. The ticket fixes the day and seed (a run that
        /// crosses midnight stays on the day it started on) and carries today's ghost, if any.
        /// </summary>
        public DailyRunTicket BeginDailyRun()
        {
            int day = DailyRunToday;
            var ticket = new DailyRunTicket
            {
                Day = day,
                Seed = DailyRuns.SeedForDay(day),
                Unrecorded = !IsRecordable(DailyState, day),
            };

            if (!ticket.Unrecorded)
            {
                ticket.Ghost = DailyRunGhost(day);
            }

            return ticket;
        }

        /// <summary>
        /// Run configuration of a Daily Run: the day's seed, no tutorial, no revive, no rank bonus, and honest gem
        /// trails (the track, gems included, must not depend on how many runs a pilot has flown).
        /// </summary>
        public RunConfig CreateDailyRunConfig(DailyRunTicket ticket)
        {
            if (ticket == null)
            {
                throw new ArgumentNullException(nameof(ticket));
            }

            return new RunConfig
            {
                Seed = ticket.Seed,
                Tutorial = false,
                ScoreBonus = DailyRuns.ScoreBonus,
                MaxRevives = DailyRuns.MaxRevives,
                StartLevel = 1,

                // Everyone rides the same track at the Standard rating; the craft is the pilot's own.
                Ship = Ship,
                Modifiers = Balance.DifficultyModifiers.Standard,
                Adaptive = false,
            };
        }

        /// <summary>
        /// Records a finished Daily Run (call right after <see cref="ApplyRun"/> with its summary): best, ghost, attempts,
        /// streak and rewards per the rules in the class remarks. Rewards are added to the wallet and to
        /// <see cref="RunSummary.TotalGemsEarned"/>; <see cref="RunSummary.Daily"/> is set. A ticket is applied at most
        /// once (a second call returns the first outcome and changes nothing).
        /// </summary>
        /// <param name="summary">The run's summary from <see cref="ApplyRun"/> (its result supplies score and distance).</param>
        /// <param name="ticket">From <see cref="BeginDailyRun"/> when the run started.</param>
        /// <param name="recorded">The run's ghost (<see cref="GhostRecorder.ToPath"/>); may be null.</param>
        public DailyRunOutcome ApplyDailyRun(RunSummary summary, DailyRunTicket ticket, GhostPath recorded)
        {
            if (ticket == null)
            {
                throw new ArgumentNullException(nameof(ticket));
            }

            if (ticket.Applied)
            {
                return ticket.Outcome;
            }

            ticket.Applied = true;
            DailyRunState s = DailyState;
            List<ShipDefinition> gatedBefore = GatedShips();
            RunResult r = summary != null ? summary.Result : null;
            var o = new DailyRunOutcome
            {
                Day = ticket.Day,
                DayKey = DailyRuns.DayKey(ticket.Day),
                Seed = ticket.Seed,
                Score = r != null ? r.Score : 0,
                Distance = r != null ? r.Distance : 0f,
            };

            ticket.Outcome = o;
            if (summary != null)
            {
                summary.Daily = o;
            }

            if (ticket.Unrecorded || !IsRecordable(s, ticket.Day))
            {
                o.ClockRejected = true;
                FillStanding(o, s, ticket.Day);
                return o;
            }

            if (r == null || !(r.Distance >= DailyRuns.MinCountedDistance))
            {
                o.TooShort = true;
                FillStanding(o, s, ticket.Day);
                return o;
            }

            if (s.Day - ticket.Day > DailyRuns.MaxClockSkewDays)
            {
                // The record is far in the future: it came from a broken clock. Re-base to today as a fresh one-day
                // streak, paying nothing for today (it is at or before a day already seen).
                StartDay(s, ticket.Day, 1);
                o.FirstRunToday = true;
                o.StreakExtended = true;
            }
            else if (ticket.Day > s.Day)
            {
                bool consecutive = s.Day > 0 && ticket.Day == s.Day + 1;
                StartDay(s, ticket.Day, consecutive ? s.Streak + 1 : 1);
                o.FirstRunToday = true;
                o.StreakExtended = true;
                o.FirstRunReward = DailyRuns.FirstRunReward;
                o.StreakReward = DailyRuns.StreakMilestoneReward(s.Streak);
                o.StreakMilestone = o.StreakReward > 0 ? s.Streak : 0;
            }

            // Same day from here on (s.Day == ticket.Day).
            o.PreviousDailyBest = s.BestScore;
            s.Attempts++;
            s.TotalRuns++;
            s.BestStreak = Math.Max(s.BestStreak, s.Streak);
            if (o.FirstRunToday || o.Score > s.BestScore)
            {
                o.NewDailyBest = true;
                s.BestScore = o.Score;
                s.BestDistance = o.Distance;
                s.BestShip = Profile.SelectedShip ?? string.Empty;
                s.Ghost = IsGhostFor(recorded, ticket.Seed) ? recorded.Encode() : string.Empty;
                o.GhostRecorded = s.Ghost.Length > 0;
            }

            if (o.Score > s.AllTimeBestScore)
            {
                s.AllTimeBestScore = o.Score;
                o.NewAllTimeBest = true;
            }

            o.Counted = true;
            FillStanding(o, s, ticket.Day);

            // A new day flown can meet a Daily Run days requirement (fleet achievements it completes are paid by the
            // next ApplyRun's evaluation, like any other unlock outside a run).
            ReportMetRequirements(gatedBefore, summary);

            int reward = o.TotalReward;
            if (reward > 0)
            {
                Profile.Gems += reward;
                if (summary != null)
                {
                    summary.TotalGemsEarned += reward;
                }

                GemsAdded?.Invoke(reward, "daily-run");
            }

            Changed?.Invoke();
            DailyRunCompleted?.Invoke(o);
            return o;
        }

        // ------------------------------------------------------------------ helpers

        private DailyRunState DailyState
        {
            get
            {
                if (Profile.DailyRun == null)
                {
                    Profile.DailyRun = new DailyRunState();
                }

                return Profile.DailyRun;
            }
        }

        private static bool IsRecordable(DailyRunState s, int day)
        {
            return DailyRuns.IsValidDay(day) && (day >= s.Day || s.Day - day > DailyRuns.MaxClockSkewDays);
        }

        private static bool IsGhostFor(GhostPath ghost, int seed)
        {
            return ghost != null && ghost.Seed == seed && ghost.TrackVersion == DailyRuns.GeneratorVersion;
        }

        private static void StartDay(DailyRunState s, int day, int streak)
        {
            s.Day = day;
            s.Streak = streak;
            s.BestScore = 0;
            s.BestDistance = 0f;
            s.BestShip = string.Empty;
            s.Attempts = 0;
            s.Ghost = string.Empty;
            s.DaysPlayed++;
        }

        private static void FillStanding(DailyRunOutcome o, DailyRunState s, int day)
        {
            bool sameDay = s.Day > 0 && s.Day == day;
            o.DailyBestScore = sameDay ? s.BestScore : 0;
            o.DailyBestDistance = sameDay ? s.BestDistance : 0f;
            o.Attempts = sameDay ? s.Attempts : 0;
            o.Streak = DailyRuns.CurrentStreak(s, day);
        }
    }
}

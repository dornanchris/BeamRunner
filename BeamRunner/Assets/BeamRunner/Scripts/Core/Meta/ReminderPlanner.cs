using System;
using BeamRunner.Core.Localization;

namespace BeamRunner.Core.Meta
{
    /// <summary>What a scheduled reminder is about (drives its text).</summary>
    public enum ReminderKind
    {
        /// <summary>Nothing to remind about.</summary>
        None = 0,

        /// <summary>The next daily reward is ready.</summary>
        DailyReward = 1,

        /// <summary>The next daily reward continues a streak of two or more days.</summary>
        DailyStreak = 2,

        /// <summary>Today's reward is still unclaimed and the streak ends at midnight.</summary>
        StreakEndsToday = 3,

        /// <summary>A Daily Run streak is at stake (only when the Daily Run feature fills it in).</summary>
        DailyRunStreak = 4,
    }

    /// <summary>
    /// Everything the planner needs, captured when the app goes to the background. The Daily Run fields are an
    /// extension point: they stay "unknown" (-1 / false) until a Daily Run feature fills them in.
    /// </summary>
    public struct ReminderContext
    {
        /// <summary>The device's local wall-clock time now.</summary>
        public DateTime LocalNow;

        /// <summary>The daily login calendar (null = treated as never claimed).</summary>
        public DailyRewardState Daily;

        /// <summary>Consecutive days with a Daily Run played, as of the last one; -1 = unknown / no Daily Run feature.</summary>
        public int DailyRunStreak;

        /// <summary>True when today's Daily Run was already played (only meaningful when <see cref="DailyRunStreak"/> ≥ 0).</summary>
        public bool DailyRunPlayedToday;

        /// <summary>
        /// Local wall-clock time at which today's Daily Run ends (its streak breaks if unplayed); default = unknown.
        /// </summary>
        public DateTime DailyRunResetLocal;

        /// <summary>A context for <paramref name="localNow"/> and the daily calendar, with no Daily Run information.</summary>
        public static ReminderContext Create(DateTime localNow, DailyRewardState daily)
        {
            return new ReminderContext
            {
                LocalNow = localNow,
                Daily = daily,
                DailyRunStreak = -1,
                DailyRunPlayedToday = false,
                DailyRunResetLocal = default(DateTime),
            };
        }
    }

    /// <summary>The single reminder to schedule (or none).</summary>
    public struct ReminderPlan
    {
        /// <summary>False when nothing should be scheduled.</summary>
        public bool HasReminder;

        /// <summary>
        /// Local wall-clock time to show it: <see cref="ReminderPlanner.PreferredHour"/>:00, or for a Daily Run streak
        /// an earlier whole hour when that run resets first; always inside the allowed window.
        /// </summary>
        public DateTime FireLocal;

        public ReminderKind Kind;
        public string Title;
        public string Body;

        /// <summary>No reminder.</summary>
        public static ReminderPlan None
        {
            get { return new ReminderPlan { HasReminder = false, Kind = ReminderKind.None, Title = string.Empty, Body = string.Empty }; }
        }
    }

    /// <summary>
    /// Plans the one local reminder the game keeps pending: "your daily reward is ready" (or "keep your streak") at
    /// <see cref="PreferredHour"/>:00 local time on the first day the next reward can be claimed — today if it is still
    /// unclaimed and there are at least <see cref="MinLeadHours"/> hours left before the evening slot, otherwise
    /// tomorrow. The time is always inside the allowed window (<see cref="EarliestHour"/>–<see cref="LatestHour"/>),
    /// never at night. A player who keeps playing never sees one (every return cancels it and the next trip to the
    /// background plans a new one); a player who stops gets exactly one.
    /// </summary>
    public static class ReminderPlanner
    {
        /// <summary>Evening slot, local time (19:00): after work/school, well before bedtime.</summary>
        public const int PreferredHour = 19;

        /// <summary>Reminders are never shown before this local hour.</summary>
        public const int EarliestHour = 9;

        /// <summary>Reminders are never shown at or after this local hour (21:00).</summary>
        public const int LatestHour = 21;

        /// <summary>A same-day reminder needs at least this many hours after the player left.</summary>
        public const double MinLeadHours = 3.0;

        /// <summary>Plans the reminder for <paramref name="context"/>. Pure and deterministic.</summary>
        public static ReminderPlan Plan(ReminderContext context)
        {
            DateTime now = context.LocalNow;
            DailyRewardState daily = context.Daily ?? new DailyRewardState();
            DateTime today = now.Date;
            bool claimedToday = !DailyRewards.CanClaim(daily, now);

            // First candidate day: today when the reward is still waiting, else tomorrow.
            DateTime day = claimedToday ? today.AddDays(1) : today;
            DateTime fire = Slot(day);
            if (fire - now < TimeSpan.FromHours(MinLeadHours))
            {
                day = day.AddDays(1);
                fire = Slot(day);
            }

            // Which reward the player would claim at that moment (the calendar itself is not changed).
            int dayIndex = DailyRewards.NextDayIndex(daily, fire);
            int amount = DailyRewards.Rewards[MathUtil.Clamp(dayIndex, 0, DailyRewards.Rewards.Length - 1)];
            int dayNumber = dayIndex + 1;
            int streakSoFar = dayIndex; // consecutive days already claimed before the reminded one

            // Texts in the current language. Sentences with two counts are two plural strings joined by the language's
            // own sentence joiner ("common.join_sentences"), so each count agrees with its own noun.
            var plan = new ReminderPlan { HasReminder = true, FireLocal = fire };
            DateTime runFire;
            if (TryDailyRunSlot(context, out runFire) && runFire <= fire)
            {
                // A Daily Run streak at risk comes first (only one reminder is ever pending).
                plan.FireLocal = runFire;
                plan.Kind = ReminderKind.DailyRunStreak;
                plan.Title = Loc.T("reminder.run_streak_title");
                plan.Body = Loc.T("reminder.run_streak_body", ("n", context.DailyRunStreak));
            }
            else if (!claimedToday && fire.Date == today && streakSoFar >= 2)
            {
                plan.Kind = ReminderKind.StreakEndsToday;
                plan.Title = Loc.T("reminder.streak_ends_title");
                plan.Body = Join(Loc.T("reminder.streak_ends_body", ("day", dayNumber), ("n", streakSoFar)),
                    Loc.T("reminder.gems_waiting", ("n", amount)));
            }
            else if (streakSoFar >= 2)
            {
                plan.Kind = ReminderKind.DailyStreak;
                plan.Title = Loc.T("reminder.streak_title", ("day", dayNumber));
                plan.Body = Join(Loc.T("reminder.streak_body", ("n", streakSoFar)), Loc.T("reminder.gems_today", ("n", amount)));
            }
            else
            {
                plan.Kind = ReminderKind.DailyReward;
                plan.Title = Loc.T("reminder.reward_title");
                plan.Body = Loc.T("reminder.reward_body", ("n", amount));
            }

            return plan;
        }

        /// <summary>
        /// The time for a Daily Run streak reminder: the evening slot today when it is before the track resets (with an
        /// hour to spare), else the latest whole hour inside the allowed window before that; always at least
        /// <see cref="MinLeadHours"/> after now. False when there is no streak at risk or no such time.
        /// </summary>
        private static bool TryDailyRunSlot(ReminderContext context, out DateTime fire)
        {
            fire = default(DateTime);
            DateTime now = context.LocalNow;
            if (context.DailyRunStreak < 2 || context.DailyRunPlayedToday || context.DailyRunResetLocal <= now)
            {
                return false;
            }

            DateTime deadline = context.DailyRunResetLocal.AddHours(-1);
            DateTime earliest = now.AddHours(MinLeadHours);
            DateTime slot = Slot(now.Date);
            if (slot >= earliest && slot <= deadline)
            {
                fire = slot;
                return true;
            }

            // Latest allowed whole hour at or before the deadline.
            DateTime candidate = new DateTime(deadline.Year, deadline.Month, deadline.Day, deadline.Hour, 0, 0, deadline.Kind);
            if (candidate.Hour >= LatestHour)
            {
                candidate = new DateTime(candidate.Year, candidate.Month, candidate.Day, LatestHour - 1, 0, 0, candidate.Kind);
            }
            else if (candidate.Hour < EarliestHour)
            {
                // Before the morning window: the previous evening's last allowed hour.
                DateTime previous = candidate.Date.AddDays(-1);
                candidate = new DateTime(previous.Year, previous.Month, previous.Day, LatestHour - 1, 0, 0, candidate.Kind);
            }

            if (candidate < earliest || candidate > deadline || !IsInAllowedWindow(candidate))
            {
                return false;
            }

            fire = candidate;
            return true;
        }

        private static string Join(string first, string second)
        {
            return Loc.T("common.join_sentences", ("a", first), ("b", second));
        }

        /// <summary>True when <paramref name="localTime"/> is inside the allowed reminder window.</summary>
        public static bool IsInAllowedWindow(DateTime localTime)
        {
            int minutes = localTime.Hour * 60 + localTime.Minute;
            return minutes >= EarliestHour * 60 && minutes < LatestHour * 60;
        }

        /// <summary>The evening slot on <paramref name="day"/> (local wall clock, clamped into the allowed window).</summary>
        public static DateTime Slot(DateTime day)
        {
            int hour = MathUtil.Clamp(PreferredHour, EarliestHour, LatestHour - 1);
            return new DateTime(day.Year, day.Month, day.Day, hour, 0, 0, day.Kind);
        }
    }
}

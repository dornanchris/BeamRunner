using System;
using System.Collections.Generic;
using BeamRunner.Core.Meta;
using NUnit.Framework;

namespace BeamRunner.Core.Tests
{
    /// <summary>Reminder scheduling, the rating prompt policy and the crash-report consent default (pure rules).</summary>
    public class PlatformFeaturesTests
    {
        private static readonly DateTime Monday = new DateTime(2026, 9, 21);

        // Reminder / mission texts come from the shipped English string table (Core/Localization).
        [SetUp]
        public void InstallEnglishStrings()
        {
            LocTestSupport.InstallEnglish();
        }

        private static DateTime At(DateTime day, int hour, int minute = 0)
        {
            return day.Date.AddHours(hour).AddMinutes(minute);
        }

        private static DailyRewardState ClaimedOn(DateTime day, int streak)
        {
            return new DailyRewardState { LastClaimDate = DailyRewards.DateKey(day), Streak = streak };
        }

        // ------------------------------------------------------------------ reminders

        [Test]
        public void Reminder_ClaimedToday_IsTomorrowEvening()
        {
            ReminderPlan plan = ReminderPlanner.Plan(ReminderContext.Create(At(Monday, 10), ClaimedOn(Monday, 1)));
            Assert.IsTrue(plan.HasReminder);
            Assert.AreEqual(At(Monday.AddDays(1), ReminderPlanner.PreferredHour), plan.FireLocal);
            Assert.AreEqual(ReminderKind.DailyReward, plan.Kind);
            StringAssert.Contains("75", plan.Body, "day 2 pays 75 gems");
        }

        [Test]
        public void Reminder_UnclaimedToday_WithTimeLeft_IsThisEvening()
        {
            ReminderPlan plan = ReminderPlanner.Plan(ReminderContext.Create(At(Monday, 9, 30), new DailyRewardState()));
            Assert.AreEqual(At(Monday, ReminderPlanner.PreferredHour), plan.FireLocal);
            Assert.AreEqual(ReminderKind.DailyReward, plan.Kind);
        }

        [Test]
        public void Reminder_UnclaimedToday_TooCloseToTheSlot_MovesToTomorrow()
        {
            ReminderPlan plan = ReminderPlanner.Plan(ReminderContext.Create(At(Monday, 17, 30), new DailyRewardState()));
            Assert.AreEqual(At(Monday.AddDays(1), ReminderPlanner.PreferredHour), plan.FireLocal);

            plan = ReminderPlanner.Plan(ReminderContext.Create(At(Monday, 23, 50), new DailyRewardState()));
            Assert.AreEqual(At(Monday.AddDays(1), ReminderPlanner.PreferredHour), plan.FireLocal);
        }

        [Test]
        public void Reminder_ClaimedToday_WithAStreak_MentionsTheStreak()
        {
            ReminderPlan plan = ReminderPlanner.Plan(ReminderContext.Create(At(Monday, 21, 30), ClaimedOn(Monday, 3)));
            Assert.AreEqual(ReminderKind.DailyStreak, plan.Kind);
            StringAssert.Contains("Day 4", plan.Title);
            StringAssert.Contains("3-day streak", plan.Body);
            StringAssert.Contains("150", plan.Body, "day 4 pays 150 gems");
        }

        [Test]
        public void Reminder_StreakEndsTonight_WhenYesterdayWasClaimed()
        {
            ReminderPlan plan = ReminderPlanner.Plan(ReminderContext.Create(At(Monday, 12), ClaimedOn(Monday.AddDays(-1), 4)));
            Assert.AreEqual(At(Monday, ReminderPlanner.PreferredHour), plan.FireLocal);
            Assert.AreEqual(ReminderKind.StreakEndsToday, plan.Kind);
            StringAssert.Contains("4-day streak", plan.Body);
        }

        [Test]
        public void Reminder_TooLateToSaveTheStreak_IsAPlainRewardTomorrow()
        {
            // Left at 18:00 without claiming after a 4-day streak: tonight is too soon, tomorrow the streak is gone.
            ReminderPlan plan = ReminderPlanner.Plan(ReminderContext.Create(At(Monday, 18), ClaimedOn(Monday.AddDays(-1), 4)));
            Assert.AreEqual(At(Monday.AddDays(1), ReminderPlanner.PreferredHour), plan.FireLocal);
            Assert.AreEqual(ReminderKind.DailyReward, plan.Kind);
            StringAssert.Contains("50", plan.Body, "a broken streak restarts at day 1 (50 gems)");
        }

        [Test]
        public void Reminder_AfterAFullWeek_WrapsToDayOne()
        {
            ReminderPlan plan = ReminderPlanner.Plan(ReminderContext.Create(At(Monday, 8), ClaimedOn(Monday, 7)));
            Assert.AreEqual(ReminderKind.DailyReward, plan.Kind);
            StringAssert.Contains("50", plan.Body);
        }

        [Test]
        public void Reminder_IsAlwaysInTheAllowedWindow_AndInTheFuture()
        {
            var states = new[]
            {
                new DailyRewardState(), ClaimedOn(Monday, 2), ClaimedOn(Monday.AddDays(-1), 5), ClaimedOn(Monday.AddDays(-3), 6),
            };
            for (int minute = 0; minute < 24 * 60; minute += 7)
            {
                foreach (DailyRewardState state in states)
                {
                    DateTime now = Monday.AddMinutes(minute);
                    ReminderPlan plan = ReminderPlanner.Plan(ReminderContext.Create(now, state));
                    Assert.IsTrue(plan.HasReminder);
                    Assert.IsTrue(ReminderPlanner.IsInAllowedWindow(plan.FireLocal), plan.FireLocal.ToString("s"));
                    Assert.GreaterOrEqual((plan.FireLocal - now).TotalHours, ReminderPlanner.MinLeadHours, now.ToString("s"));
                    Assert.LessOrEqual((plan.FireLocal - now).TotalDays, 2.0, now.ToString("s"));
                    Assert.IsFalse(string.IsNullOrEmpty(plan.Title));
                    Assert.IsFalse(string.IsNullOrEmpty(plan.Body));
                }
            }
        }

        [Test]
        public void Reminder_DoesNotChangeTheCalendar()
        {
            DailyRewardState state = ClaimedOn(Monday, 2);
            ReminderPlanner.Plan(ReminderContext.Create(At(Monday, 10), state));
            Assert.AreEqual(DailyRewards.DateKey(Monday), state.LastClaimDate);
            Assert.AreEqual(2, state.Streak);
        }

        [Test]
        public void Reminder_DailyRunStreakAtRisk_ComesFirst_BeforeTheReset()
        {
            ReminderContext c = ReminderContext.Create(At(Monday, 10), ClaimedOn(Monday, 1));
            c.DailyRunStreak = 5;
            c.DailyRunPlayedToday = false;
            c.DailyRunResetLocal = At(Monday.AddDays(1), 2); // e.g. UTC midnight = 02:00 local
            ReminderPlan plan = ReminderPlanner.Plan(c);
            Assert.AreEqual(ReminderKind.DailyRunStreak, plan.Kind);
            Assert.AreEqual(At(Monday, ReminderPlanner.PreferredHour), plan.FireLocal);
            StringAssert.Contains("5-day", plan.Body);

            // The run resets at 16:00 local: remind at 15:00 at the latest, if that is still 3 h away.
            c.DailyRunResetLocal = At(Monday, 16);
            c.LocalNow = At(Monday, 9);
            plan = ReminderPlanner.Plan(c);
            Assert.AreEqual(ReminderKind.DailyRunStreak, plan.Kind);
            Assert.AreEqual(At(Monday, 15), plan.FireLocal);

            // Too late for that: fall back to the daily reward reminder.
            c.LocalNow = At(Monday, 13);
            plan = ReminderPlanner.Plan(c);
            Assert.AreEqual(ReminderKind.DailyReward, plan.Kind);

            // Played already, or no streak worth keeping: no Daily Run reminder.
            c.LocalNow = At(Monday, 9);
            c.DailyRunPlayedToday = true;
            Assert.AreNotEqual(ReminderKind.DailyRunStreak, ReminderPlanner.Plan(c).Kind);
            c.DailyRunPlayedToday = false;
            c.DailyRunStreak = 1;
            Assert.AreNotEqual(ReminderKind.DailyRunStreak, ReminderPlanner.Plan(c).Kind);
        }

        [Test]
        public void Reminder_DailyRunResetInTheMorning_UsesTheMorningWindow()
        {
            // Reset at 10:00 local (UTC+10), leaving at 20:00: tonight's slot has passed, so the reminder goes out at
            // 09:00 tomorrow, the start of the allowed window and an hour before the reset.
            ReminderContext c = ReminderContext.Create(At(Monday, 20), ClaimedOn(Monday, 1));
            c.DailyRunStreak = 3;
            c.DailyRunResetLocal = At(Monday.AddDays(1), 10);
            ReminderPlan plan = ReminderPlanner.Plan(c);
            Assert.AreEqual(ReminderKind.DailyRunStreak, plan.Kind);
            Assert.AreEqual(At(Monday.AddDays(1), 9), plan.FireLocal);
        }

        // ------------------------------------------------------------------ rating prompt policy

        /// <summary>Launches 1..<paramref name="launches"/>, answering every due prompt with <paramref name="answer"/>.</summary>
        private static List<int> DueLaunches(ReviewPromptState s, int launches, ReviewAnswer answer, bool crashedAtLaunch6 = false)
        {
            var due = new List<int>();
            for (int i = 1; i <= launches; i++)
            {
                if (ReviewPromptPolicy.RecordLaunch(s, crashedAtLaunch6 && i == 6))
                {
                    due.Add(i);
                    ReviewPromptPolicy.MarkRequested(s, At(Monday, 12).AddDays(i));
                    ReviewPromptPolicy.Answer(s, answer);
                }
            }

            return due;
        }

        [Test]
        public void RatingPrompt_EveryThirdLaunch_WhileThePlayerSaysNotNow()
        {
            var s = new ReviewPromptState();
            CollectionAssert.AreEqual(new[] { 3, 6, 9, 12 }, DueLaunches(s, 13, ReviewAnswer.Later));
            Assert.AreEqual(13, s.Launches);
            Assert.AreEqual(4, s.Requests);
            Assert.IsFalse(s.OptedOut);
            Assert.AreEqual(At(Monday, 12).AddDays(12).Ticks, s.LastRequestUtcTicks);
        }

        [Test]
        public void RatingPrompt_DontAskAgainAndRateNow_EndIt()
        {
            var never = new ReviewPromptState();
            CollectionAssert.AreEqual(new[] { 3 }, DueLaunches(never, 30, ReviewAnswer.Never), "DON'T ASK AGAIN");
            Assert.IsTrue(never.OptedOut);

            var rated = new ReviewPromptState();
            CollectionAssert.AreEqual(new[] { 3 }, DueLaunches(rated, 30, ReviewAnswer.Rate), "RATE NOW: the player went to the store");
            Assert.IsTrue(rated.OptedOut);
            Assert.IsFalse(ReviewPromptPolicy.IsDue(rated, false));
        }

        [Test]
        public void RatingPrompt_SkipsTheLaunchAfterAnAppCrash()
        {
            var s = new ReviewPromptState();
            CollectionAssert.AreEqual(new[] { 3, 9 }, DueLaunches(s, 10, ReviewAnswer.Later, true));
        }

        [Test]
        public void RatingPrompt_SurvivesBadStoredValues()
        {
            var s = new ReviewPromptState { Launches = -7 };
            Assert.IsFalse(ReviewPromptPolicy.RecordLaunch(s, false));
            Assert.AreEqual(1, s.Launches, "a corrupt count starts again");

            s.Launches = int.MaxValue;
            ReviewPromptPolicy.RecordLaunch(s, false);
            Assert.AreEqual(int.MaxValue, s.Launches, "no overflow");

            Assert.IsFalse(ReviewPromptPolicy.RecordLaunch(null, false));
            Assert.IsFalse(ReviewPromptPolicy.IsDue(null, false));
            ReviewPromptPolicy.Answer(null, ReviewAnswer.Never);
            ReviewPromptPolicy.MarkRequested(null, At(Monday, 12));
        }

        // ------------------------------------------------------------------ crash-report consent default

        [Test]
        public void CrashReports_OptInInTheEeaUkAndSwitzerland()
        {
            Assert.IsTrue(PrivacyRegion.RequiresOptIn("DE", "Europe/Berlin"));
            Assert.IsTrue(PrivacyRegion.RequiresOptIn("fr", null));
            Assert.IsTrue(PrivacyRegion.RequiresOptIn("GB", "Europe/London"));
            Assert.IsTrue(PrivacyRegion.RequiresOptIn("CH", "Europe/Zurich"));
            Assert.IsTrue(PrivacyRegion.RequiresOptIn("NO", "Europe/Oslo"));
            Assert.IsTrue(PrivacyRegion.RequiresOptIn("RE", "Indian/Reunion"));
            Assert.IsTrue(PrivacyRegion.RequiresOptIn(null, "Atlantic/Canary"));
        }

        [Test]
        public void CrashReports_OptInWhenEitherSignalIsEuropean_OrNothingIsKnown()
        {
            Assert.IsTrue(PrivacyRegion.RequiresOptIn("US", "Europe/Paris"), "travelling / mismatched settings");
            Assert.IsTrue(PrivacyRegion.RequiresOptIn("IT", "America/New_York"));
            Assert.IsTrue(PrivacyRegion.RequiresOptIn(null, null));
            Assert.IsTrue(PrivacyRegion.RequiresOptIn("", "UTC"));
            Assert.IsTrue(PrivacyRegion.RequiresOptIn("IV", "Etc/GMT+3"), "invariant culture region = unknown");
            Assert.IsTrue(PrivacyRegion.RequiresOptIn(null, "W. Europe Standard Time"), "Windows zone id (Editor)");
        }

        [Test]
        public void CrashReports_OptOutElsewhere()
        {
            Assert.IsFalse(PrivacyRegion.RequiresOptIn("US", "America/Los_Angeles"));
            Assert.IsFalse(PrivacyRegion.RequiresOptIn("JP", "Asia/Tokyo"));
            Assert.IsFalse(PrivacyRegion.RequiresOptIn("BR", null));
            Assert.IsFalse(PrivacyRegion.RequiresOptIn(null, "Australia/Sydney"));
            Assert.IsTrue(PrivacyRegion.DefaultCrashReportsOn("CA", "America/Toronto"));
            Assert.IsFalse(PrivacyRegion.DefaultCrashReportsOn("AT", "Europe/Vienna"));
        }
    }
}

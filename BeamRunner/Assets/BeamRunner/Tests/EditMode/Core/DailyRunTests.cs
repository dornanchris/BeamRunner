using System;
using System.Collections.Generic;
using BeamRunner.Core.Balance;
using BeamRunner.Core.Field;
using BeamRunner.Core.Meta;
using BeamRunner.Core.Sim;
using NUnit.Framework;

namespace BeamRunner.Core.Tests
{
    public class DailyRunTests
    {
        private static readonly DateTime Sep26 = new DateTime(2026, 9, 26, 0, 0, 0, DateTimeKind.Utc);

        // Golden values: the Daily Run is only "the same field for everyone" while these hold. If a change to the seed
        // mix or the Rng breaks them, bump DailyRuns.GeneratorVersion (old ghosts are then ignored; online daily
        // leaderboards should be keyed by it) and update the constants.
        // The track itself is not pinned yet: the building-runner generator (TrackGenerator, ChunkLibrary, Levels) is
        // still being tuned. Before the first release, pin TrackFingerprint(SeedForDay(Sep 26 2026), 4000) here as
        // GoldenTrackSep26, assert it in DailyTrack_IsDeterministic_SameDaySameTrack_DifferentDaysDiffer, and bump
        // DailyRuns.GeneratorVersion whenever it has to change.
        private const int GoldenDaySep26 = 20722;
        private const int GoldenSeedSep26 = 488620138;

        // ------------------------------------------------------------------ calendar & seed

        [Test]
        public void DayIndex_IsTheUtcDate_ExactAtMidnight()
        {
            int day = DailyRuns.DayIndex(Sep26);
            Assert.AreEqual(GoldenDaySep26, day);
            Assert.AreEqual(day, DailyRuns.DayIndex(Sep26.AddTicks(TimeSpan.TicksPerDay - 1)), "23:59:59.9999999 is still the same day");
            Assert.AreEqual(day + 1, DailyRuns.DayIndex(Sep26.AddDays(1)), "00:00:00 is the next day");
            Assert.AreEqual(day - 1, DailyRuns.DayIndex(Sep26.AddTicks(-1)));
            Assert.AreEqual(0, DailyRuns.DayIndex(new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
            Assert.AreEqual(-1, DailyRuns.DayIndex(new DateTime(1969, 12, 31, 23, 59, 0, DateTimeKind.Utc)));
            Assert.AreEqual(Sep26, DailyRuns.DayStartUtc(day));
            Assert.AreEqual("2026-09-26", DailyRuns.DayKey(day));
            Assert.DoesNotThrow(() => DailyRuns.DayKey(int.MaxValue), "tampered day numbers must not throw");
            Assert.DoesNotThrow(() => DailyRuns.DayKey(int.MinValue));
            Assert.IsFalse(DailyRuns.IsValidDay(0));
            Assert.IsTrue(DailyRuns.IsValidDay(day));
        }

        [Test]
        public void DayIndex_SameInstant_SameDayInEveryTimeZone()
        {
            // 2026-09-27 09:30 UTC, seen from Honolulu (−10), Kiribati (+14), Nepal (+5:45) and UTC.
            DateTimeOffset utc = new DateTimeOffset(2026, 9, 27, 9, 30, 0, TimeSpan.Zero);
            DateTimeOffset honolulu = new DateTimeOffset(2026, 9, 26, 23, 30, 0, TimeSpan.FromHours(-10));
            DateTimeOffset kiribati = new DateTimeOffset(2026, 9, 27, 23, 30, 0, TimeSpan.FromHours(14));
            DateTimeOffset nepal = new DateTimeOffset(2026, 9, 27, 15, 15, 0, new TimeSpan(5, 45, 0));
            int day = DailyRuns.DayIndex(utc);
            Assert.AreEqual(GoldenDaySep26 + 1, day);
            Assert.AreEqual(day, DailyRuns.DayIndex(honolulu), "local date is still the 26th in Honolulu");
            Assert.AreEqual(day, DailyRuns.DayIndex(kiribati));
            Assert.AreEqual(day, DailyRuns.DayIndex(nepal));

            // A Local-kind reading of the same instant (whatever this machine's zone) maps to the same UTC day.
            DateTime local = utc.UtcDateTime.ToLocalTime();
            Assert.AreEqual(DateTimeKind.Local, local.Kind);
            Assert.AreEqual(day, DailyRuns.DayIndex(local));
            Assert.AreEqual(DailyRuns.SeedForDay(day), DailyRuns.SeedForDay(DailyRuns.DayIndex(honolulu)));
        }

        [Test]
        public void Seed_IsPinnedForADate()
        {
            int day = DailyRuns.DayIndex(Sep26);
            Assert.AreEqual(GoldenSeedSep26, DailyRuns.SeedForDay(day), "the Sep 26 2026 seed changed (see the golden-value note)");
            Assert.AreEqual(DailyRuns.SeedForDay(day), DailyRuns.SeedForDay(DailyRuns.DayIndex(Sep26.AddHours(13.5))));
        }

        [Test]
        public void Seed_DiffersForEveryPlausibleDay()
        {
            var seen = new HashSet<int>();
            for (int day = DailyRuns.MinValidDay; day <= DailyRuns.MaxValidDay; day++)
            {
                Assert.IsTrue(seen.Add(DailyRuns.SeedForDay(day)), "day " + day + " repeats a seed");
            }

            Assert.That(seen.Count, Is.GreaterThan(60000));
        }

        [Test]
        public void DailyTrack_IsDeterministic_SameDaySameTrack_DifferentDaysDiffer()
        {
            int day = DailyRuns.DayIndex(Sep26);
            uint a = TrackFingerprint(DailyRuns.SeedForDay(day), 4000);
            uint b = TrackFingerprint(DailyRuns.SeedForDay(DailyRuns.DayIndex(Sep26.AddHours(23))), 4000);
            Assert.AreEqual(a, b, "same day, same track");
            Assert.AreEqual(a, TrackFingerprint(DailyRuns.SeedForDay(day), 4000), "a second generator lays the same track");
            Assert.AreNotEqual(TrackFingerprint(DailyRuns.SeedForDay(day), 2000), a, "the fingerprint covers the whole length");

            var tracks = new HashSet<uint>();
            for (int d = day; d < day + 60; d++)
            {
                Assert.IsTrue(tracks.Add(TrackFingerprint(DailyRuns.SeedForDay(d), 1500)), "day " + d + " repeats a track");
            }
        }

        [Test]
        public void DailyTrack_SimulationIsDeterministic()
        {
            // Two full simulations of the same day's run with the same inputs end identically (frame by frame).
            ProfileService morning = NewService(Sep26.AddHours(1));
            ProfileService evening = NewService(Sep26.AddHours(20));
            var a = new RunSimulation(morning.CreateDailyRunConfig(morning.BeginDailyRun()));
            var b = new RunSimulation(evening.CreateDailyRunConfig(evening.BeginDailyRun()));
            for (int i = 0; i < 60 * 60 && !a.IsCrashed; i++)
            {
                a.Step(1f / 60f, Diagnostics.RunAutopilot.FollowLine(a));
                b.Step(1f / 60f, Diagnostics.RunAutopilot.FollowLine(b));
                a.ClearEvents();
                b.ClearEvents();
                Assert.AreEqual(a.AtCheckpoint, b.AtCheckpoint, "frame " + i);
                if (a.AtCheckpoint)
                {
                    // The Daily Run waits at each wall for the pilot: both continue with the same perk.
                    a.ContinueFromCheckpoint(CheckpointPerk.Refuel);
                    b.ContinueFromCheckpoint(CheckpointPerk.Refuel);
                }
            }

            Assert.AreEqual(a.Distance, b.Distance);
            Assert.AreEqual(a.Score, b.Score);
            Assert.AreEqual(a.ShipX, b.ShipX);
            Assert.AreEqual(a.ShipY, b.ShipY);
            Assert.AreEqual(a.Fuel, b.Fuel);
            Assert.AreEqual(a.Hull, b.Hull);
            Assert.AreEqual(a.IsCrashed, b.IsCrashed);
            Assert.AreEqual(a.BuildResult().Builds, b.BuildResult().Builds);
            Assert.That(a.Distance, Is.GreaterThan(600.0), "at least half a minute of flying");
        }

        [Test]
        public void Countdown_RunsToTheNextUtcMidnight()
        {
            DateTime now = new DateTime(2026, 9, 26, 18, 17, 30, DateTimeKind.Utc);
            Assert.AreEqual(new DateTime(2026, 9, 27, 0, 0, 0, DateTimeKind.Utc), DailyRuns.NextResetUtc(now));
            Assert.AreEqual(new TimeSpan(5, 42, 30), DailyRuns.TimeUntilReset(now));
            Assert.AreEqual(TimeSpan.FromDays(1), DailyRuns.TimeUntilReset(Sep26), "at midnight a whole day is left");

            ProfileService s = NewService(now);
            Assert.AreEqual(DailyRuns.NextResetUtc(now), s.DailyRunNextResetUtc);
            Assert.AreEqual(new TimeSpan(5, 42, 30), s.DailyRunTimeUntilReset);
        }

        [Test]
        public void DailyConfig_NoTutorialNoReviveNoRankBonus()
        {
            ProfileService s = NewService(Sep26);
            s.Profile.PilotRank = 9;
            s.Profile.Flags.TutorialDone = false;
            DailyRunTicket ticket = s.BeginDailyRun();
            RunConfig c = s.CreateDailyRunConfig(ticket);
            Assert.AreEqual(DailyRuns.SeedForDay(DailyRuns.DayIndex(Sep26)), c.Seed);
            Assert.IsFalse(c.Tutorial);
            Assert.IsFalse(c.Adaptive, "one track for everybody: the Daily Run never moves or uses the hidden rating");
            Assert.AreEqual(0f, c.Modifiers.Rating, "Standard difficulty");
            Assert.AreEqual(1f, c.Modifiers.Gaps);
            Assert.AreEqual(1f, c.Modifiers.Fuel);
            Assert.AreEqual(1f, c.Modifiers.Speed);
            Assert.AreEqual(0, c.MaxRevives);
            Assert.AreEqual(0f, c.ScoreBonus);
            Assert.AreEqual(1, c.StartLevel, "every pilot flies the daily field from level 1");
            Assert.That(s.CreateRunConfig(1).ScoreBonus, Is.GreaterThan(0f), "normal runs keep the rank bonus");

            var sim = new RunSimulation(c);
            Assert.IsFalse(sim.CanRevive);
        }

        [Test]
        public void DailyRun_UnlocksWithTheFirstFlight()
        {
            ProfileService s = new ProfileService(PlayerProfile.CreateNew(), null, 3);
            Assert.IsFalse(s.DailyRunUnlocked);
            s.ApplyRun(new RunResult { Score = 500, Distance = 400f });
            Assert.IsTrue(s.DailyRunUnlocked);
        }

        // ------------------------------------------------------------------ attempts, best, rewards

        [Test]
        public void FirstRunOfTheDay_PaysOnce_LaterAttemptsOnlyImproveTheBest()
        {
            var h = new Harness(Sep26.AddHours(9));
            long before = h.Service.Gems;
            DailyRunOutcome first = h.Play(4000);
            Assert.IsTrue(first.Counted);
            Assert.IsTrue(first.FirstRunToday);
            Assert.IsTrue(first.NewDailyBest);
            Assert.AreEqual(DailyRuns.FirstRunReward, first.FirstRunReward);
            Assert.AreEqual(0, first.StreakReward);
            Assert.AreEqual(1, first.Streak);
            Assert.AreEqual(1, first.Attempts);
            Assert.AreEqual(DailyRuns.FirstRunReward, h.LastDailyGems);
            Assert.AreEqual(4000, h.Service.DailyRunBestScoreToday);
            Assert.IsTrue(h.Service.DailyRunPlayedToday);

            DailyRunOutcome worse = h.Play(2500);
            Assert.IsTrue(worse.Counted);
            Assert.IsFalse(worse.FirstRunToday);
            Assert.IsFalse(worse.NewDailyBest);
            Assert.AreEqual(0, worse.TotalReward);
            Assert.AreEqual(0, h.LastDailyGems);
            Assert.AreEqual(4000, worse.DailyBestScore);
            Assert.AreEqual(4000, worse.PreviousDailyBest);
            Assert.AreEqual(2, worse.Attempts);

            h.Utc = Sep26.AddHours(23.99);
            DailyRunOutcome better = h.Play(9000, 1500f);
            Assert.IsTrue(better.NewDailyBest);
            Assert.AreEqual(0, better.TotalReward, "only the first run of a day pays");
            Assert.AreEqual(9000, h.Service.DailyRunBestScoreToday);
            Assert.AreEqual(1500f, h.Service.DailyRunBestDistanceToday);
            Assert.AreEqual(3, h.Service.DailyRunAttemptsToday);
            Assert.AreEqual(1, h.Service.DailyRunStreak);

            // Everything the daily rules paid over the day: exactly one first-run reward.
            Assert.AreEqual(DailyRuns.FirstRunReward, h.Service.Gems - before - h.RunGems);
        }

        [Test]
        public void Ticket_IsAppliedOnlyOnce()
        {
            var h = new Harness(Sep26.AddHours(12));
            DailyRunTicket ticket = h.Service.BeginDailyRun();
            RunSummary summary = h.Service.ApplyRun(Result(3000, 900f));
            DailyRunOutcome a = h.Service.ApplyDailyRun(summary, ticket, null);
            long gems = h.Service.Gems;
            long earned = summary.TotalGemsEarned;
            DailyRunOutcome b = h.Service.ApplyDailyRun(summary, ticket, null);
            Assert.AreSame(a, b);
            Assert.AreSame(a, summary.Daily);
            Assert.AreEqual(gems, h.Service.Gems);
            Assert.AreEqual(earned, summary.TotalGemsEarned);
            Assert.AreEqual(1, h.Service.DailyRunAttemptsToday);
        }

        [Test]
        public void RewardsAreAddedToTheRunSummary()
        {
            var h = new Harness(Sep26.AddHours(12));
            DailyRunTicket ticket = h.Service.BeginDailyRun();
            RunSummary summary = h.Service.ApplyRun(Result(3000, 900f));
            long runOnly = summary.TotalGemsEarned;
            DailyRunOutcome o = h.Service.ApplyDailyRun(summary, ticket, null);
            Assert.AreEqual(runOnly + o.TotalReward, summary.TotalGemsEarned);
        }

        [Test]
        public void TooShortRuns_DoNotCount()
        {
            var h = new Harness(Sep26.AddHours(12));
            DailyRunOutcome o = h.Play(300, DailyRuns.MinCountedDistance - 1f);
            Assert.IsFalse(o.Counted);
            Assert.IsTrue(o.TooShort);
            Assert.AreEqual(0, o.TotalReward);
            Assert.AreEqual(0, h.LastDailyGems);
            Assert.IsFalse(h.Service.DailyRunPlayedToday);
            Assert.AreEqual(0, h.Service.DailyRunStreak);
            Assert.AreEqual(0, h.Events.Count);

            Assert.IsTrue(h.Play(900, DailyRuns.MinCountedDistance).Counted);
            Assert.AreEqual(1, h.Events.Count);
        }

        [Test]
        public void StreakMilestones_PayOnTheDayTheyAreReached()
        {
            Assert.AreEqual(0, DailyRuns.StreakMilestoneReward(1));
            Assert.AreEqual(100, DailyRuns.StreakMilestoneReward(3));
            Assert.AreEqual(250, DailyRuns.StreakMilestoneReward(7));
            Assert.AreEqual(400, DailyRuns.StreakMilestoneReward(14));
            Assert.AreEqual(800, DailyRuns.StreakMilestoneReward(30));
            Assert.AreEqual(0, DailyRuns.StreakMilestoneReward(31));
            Assert.AreEqual(800, DailyRuns.StreakMilestoneReward(60));
            Assert.AreEqual(800, DailyRuns.StreakMilestoneReward(90));
            Assert.AreEqual(3, DailyRuns.NextStreakMilestone(0));
            Assert.AreEqual(7, DailyRuns.NextStreakMilestone(3));
            Assert.AreEqual(30, DailyRuns.NextStreakMilestone(14));
            Assert.AreEqual(60, DailyRuns.NextStreakMilestone(30));

            var h = new Harness(Sep26.AddHours(8));
            var paid = new List<int>();
            int total = 0;
            for (int i = 1; i <= 61; i++)
            {
                DailyRunOutcome o = h.Play(1000 + i);
                Assert.AreEqual(i, o.Streak, "day " + i);
                Assert.AreEqual(DailyRuns.FirstRunReward, o.FirstRunReward);
                if (o.StreakReward > 0)
                {
                    paid.Add(o.StreakMilestone);
                }

                total += o.TotalReward;

                // A second attempt the same day never pays again.
                h.Utc = h.Utc.AddHours(3);
                Assert.AreEqual(0, h.Play(10).TotalReward);
                h.Utc = h.Utc.AddHours(21);
            }

            CollectionAssert.AreEqual(new[] { 3, 7, 14, 30, 60 }, paid);
            Assert.AreEqual(61 * DailyRuns.FirstRunReward + 100 + 250 + 400 + 800 + 800, total);
            Assert.AreEqual(61, h.Service.Profile.DailyRun.BestStreak);
            Assert.AreEqual(61, h.Service.Profile.DailyRun.DaysPlayed);
            Assert.AreEqual(122, h.Service.Profile.DailyRun.TotalRuns);
        }

        // ------------------------------------------------------------------ streak across days / zones / clocks

        [Test]
        public void Streak_ConsecutiveUtcDays_AMissedDayRestartsIt()
        {
            var h = new Harness(Sep26.AddHours(10));
            Assert.AreEqual(1, h.Play(100).Streak);
            h.Utc = h.Utc.AddDays(1);
            Assert.AreEqual(2, h.Play(100).Streak);
            h.Utc = h.Utc.AddDays(1);
            DailyRunOutcome third = h.Play(100);
            Assert.AreEqual(3, third.Streak);
            Assert.AreEqual(3, third.StreakMilestone);

            // Day 4 not played: still alive (at risk) on day 4, gone on day 5.
            h.Utc = h.Utc.AddDays(1);
            Assert.AreEqual(3, h.Service.DailyRunStreak);
            Assert.IsTrue(h.Service.DailyRunStreakAtRisk);
            Assert.IsFalse(h.Service.DailyRunPlayedToday);
            h.Utc = h.Utc.AddDays(1);
            Assert.AreEqual(0, h.Service.DailyRunStreak);
            Assert.IsFalse(h.Service.DailyRunStreakAtRisk);

            DailyRunOutcome restart = h.Play(100);
            Assert.AreEqual(1, restart.Streak);
            Assert.IsTrue(restart.StreakExtended);
            Assert.AreEqual(DailyRuns.FirstRunReward, restart.TotalReward);
            Assert.AreEqual(3, h.Service.Profile.DailyRun.BestStreak);
        }

        [Test]
        public void Streak_OneSecondApartAcrossUtcMidnight_AreTwoDays()
        {
            var h = new Harness(Sep26.AddDays(1).AddSeconds(-1));
            Assert.AreEqual(1, h.Play(100).Streak);
            h.Utc = Sep26.AddDays(1);
            DailyRunOutcome next = h.Play(100);
            Assert.AreEqual(2, next.Streak);
            Assert.AreEqual(DailyRuns.FirstRunReward, next.FirstRunReward);
        }

        [Test]
        public void Streak_FollowsUtcDays_NotThePlayersLocalCalendar()
        {
            // A player in California (UTC−7 in September).
            TimeSpan pdt = TimeSpan.FromHours(-7);
            var h = new Harness(new DateTimeOffset(2026, 9, 25, 20, 0, 0, pdt).UtcDateTime); // Sep 26 03:00 UTC
            DailyRunOutcome evening = h.Play(1000);
            Assert.AreEqual(DailyRuns.DayIndex(Sep26), evening.Day);

            // Next local morning is still Sep 26 UTC: same track, no second reward, streak unchanged.
            h.Utc = new DateTimeOffset(2026, 9, 26, 9, 0, 0, pdt).UtcDateTime; // 16:00 UTC
            DailyRunOutcome morning = h.Play(1200);
            Assert.AreEqual(evening.Day, morning.Day);
            Assert.AreEqual(evening.Seed, morning.Seed);
            Assert.AreEqual(0, morning.TotalReward);
            Assert.AreEqual(1, morning.Streak);

            // Same local day, 17:30 = Sep 27 00:30 UTC: a new track and day 2 of the streak.
            h.Utc = new DateTimeOffset(2026, 9, 26, 17, 30, 0, pdt).UtcDateTime;
            DailyRunOutcome later = h.Play(900);
            Assert.AreEqual(evening.Day + 1, later.Day);
            Assert.AreEqual(2, later.Streak);
            Assert.AreEqual(DailyRuns.FirstRunReward, later.FirstRunReward);

            // Flying to Tokyo (UTC+9) changes nothing: the next run at local Sep 27 23:00 is Sep 27 14:00 UTC.
            h.Utc = new DateTimeOffset(2026, 9, 27, 23, 0, 0, TimeSpan.FromHours(9)).UtcDateTime;
            DailyRunOutcome tokyo = h.Play(950);
            Assert.AreEqual(later.Day, tokyo.Day);
            Assert.AreEqual(0, tokyo.TotalReward);
            Assert.AreEqual(2, tokyo.Streak);
        }

        [Test]
        public void RunCrossingMidnight_BelongsToTheDayItStarted()
        {
            var h = new Harness(Sep26.AddHours(23).AddMinutes(58));
            DailyRunTicket ticket = h.Service.BeginDailyRun();
            h.Utc = Sep26.AddDays(1).AddMinutes(3); // finished after midnight
            RunSummary summary = h.Service.ApplyRun(Result(5000, 2000f));
            DailyRunOutcome o = h.Service.ApplyDailyRun(summary, ticket, null);
            Assert.AreEqual(DailyRuns.DayIndex(Sep26), o.Day);
            Assert.AreEqual("2026-09-26", o.DayKey);
            Assert.IsTrue(o.Counted);

            // The new day's first run is still a new day: streak 2 and its own reward.
            DailyRunOutcome next = h.Play(100);
            Assert.AreEqual(o.Day + 1, next.Day);
            Assert.AreEqual(2, next.Streak);
            Assert.AreEqual(DailyRuns.FirstRunReward, next.FirstRunReward);
        }

        [Test]
        public void ClockGoingBackwards_RecordsAndPaysNothing()
        {
            var h = new Harness(Sep26.AddHours(10));
            h.Play(1000);
            h.Utc = h.Utc.AddDays(1);
            h.Play(3000); // streak 2, best 3000 on Sep 27
            DailyState before = DailyState.Of(h.Service);

            h.Utc = h.Utc.AddDays(-1); // back to Sep 26
            Assert.IsFalse(h.Service.DailyRunRecordable);
            DailyRunTicket ticket = h.Service.BeginDailyRun();
            Assert.IsTrue(ticket.Unrecorded);
            Assert.IsNull(ticket.Ghost);
            RunSummary summary = h.Service.ApplyRun(Result(99999, 5000f));
            long gems = h.Service.Gems;
            DailyRunOutcome o = h.Service.ApplyDailyRun(summary, ticket, null);
            Assert.IsFalse(o.Counted);
            Assert.IsTrue(o.ClockRejected);
            Assert.AreEqual(0, o.TotalReward);
            Assert.AreEqual(gems, h.Service.Gems);
            Assert.AreEqual(before, DailyState.Of(h.Service), "nothing recorded");
            Assert.AreEqual(2, h.Events.Count, "no DailyRunCompleted for rejected runs");

            // Back on Sep 27: a normal same-day attempt (no reward); Sep 28 continues the streak.
            h.Utc = h.Utc.AddDays(1);
            Assert.AreEqual(0, h.Play(100).TotalReward);
            h.Utc = h.Utc.AddDays(1);
            DailyRunOutcome resumed = h.Play(100);
            Assert.AreEqual(3, resumed.Streak);
            Assert.AreEqual(DailyRuns.FirstRunReward + 100, resumed.TotalReward);
        }

        [Test]
        public void ClockGoingBackwardsWithinADay_IsJustAnotherAttempt()
        {
            var h = new Harness(Sep26.AddHours(18));
            h.Play(1000);
            h.Utc = Sep26.AddHours(6);
            DailyRunOutcome o = h.Play(2000);
            Assert.IsTrue(o.Counted);
            Assert.AreEqual(0, o.TotalReward);
            Assert.AreEqual(2, o.Attempts);
            Assert.AreEqual(2000, o.DailyBestScore);
        }

        [Test]
        public void RecordFarInTheFuture_IsRebasedWithoutAReward()
        {
            // The device once ran with a clock in 2037 and recorded a Daily Run there.
            var h = new Harness(new DateTime(2037, 5, 1, 12, 0, 0, DateTimeKind.Utc));
            h.Play(1000);
            h.Utc = Sep26.AddHours(12);
            Assert.IsTrue(h.Service.DailyRunRecordable, "a record years ahead of the clock must not lock the player out");
            DailyRunOutcome o = h.Play(2000);
            Assert.IsTrue(o.Counted);
            Assert.IsTrue(o.FirstRunToday);
            Assert.AreEqual(0, o.TotalReward, "no reward for a day at or before one already seen");
            Assert.AreEqual(1, o.Streak);
            Assert.AreEqual(2000, h.Service.DailyRunBestScoreToday);

            h.Utc = h.Utc.AddDays(1);
            DailyRunOutcome next = h.Play(100);
            Assert.AreEqual(2, next.Streak);
            Assert.AreEqual(DailyRuns.FirstRunReward, next.TotalReward);
        }

        [Test]
        public void ImplausibleClocks_NeverRecord()
        {
            foreach (DateTime clock in new[]
                     {
                         new DateTime(1970, 1, 1, 0, 0, 5, DateTimeKind.Utc),
                         new DateTime(2019, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                         new DateTime(2301, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                     })
            {
                var h = new Harness(clock);
                DailyRunOutcome o = h.Play(5000);
                Assert.IsFalse(o.Counted, clock.ToString("o"));
                Assert.IsTrue(o.ClockRejected);
                Assert.AreEqual(0, h.LastDailyGems);
                Assert.AreEqual(0, h.Service.Profile.DailyRun.Day);
            }
        }

        [Test]
        public void CompletedEvent_OnlyForCountedRuns_WithLeaderboardData()
        {
            var h = new Harness(Sep26.AddHours(7));
            h.Play(4321, 1234f);
            h.Play(50, 20f);
            Assert.AreEqual(1, h.Events.Count);
            DailyRunOutcome e = h.Events[0];
            Assert.AreEqual(4321, e.Score);
            Assert.AreEqual(1234f, e.Distance);
            Assert.AreEqual("2026-09-26", e.DayKey);
            Assert.AreEqual(DailyRuns.SeedForDay(DailyRuns.DayIndex(Sep26)), e.Seed);
            Assert.AreEqual(DailyRuns.GeneratorVersion, e.GeneratorVersion);
        }

        // ------------------------------------------------------------------ ghost storage

        [Test]
        public void Ghost_IsTheDaysBestRun_AndOnlyServedForThatDay()
        {
            var h = new Harness(Sep26.AddHours(11));
            DailyRunTicket first = h.Service.BeginDailyRun();
            Assert.IsNull(first.Ghost, "no ghost before the first run");
            GhostPath ghostA = RecordLine(first.Seed, 600.0, 0.3f);
            DailyRunOutcome a = h.Apply(first, 3000, ghostA);
            Assert.IsTrue(a.GhostRecorded);
            Assert.AreEqual(ghostA.Encode(), h.Service.Profile.DailyRun.Ghost);

            DailyRunTicket second = h.Service.BeginDailyRun();
            Assert.IsNotNull(second.Ghost);
            Assert.AreEqual(ghostA.Encode(), second.Ghost.Encode());

            // A worse run keeps the ghost; a better one replaces it; a ghost of another track is never stored.
            h.Apply(second, 1000, RecordLine(second.Seed, 300.0, -0.5f));
            Assert.AreEqual(ghostA.Encode(), h.Service.Profile.DailyRun.Ghost);
            DailyRunTicket third = h.Service.BeginDailyRun();
            GhostPath ghostC = RecordLine(third.Seed, 900.0, 1.1f);
            h.Apply(third, 8000, ghostC);
            Assert.AreEqual(ghostC.Encode(), h.Service.Profile.DailyRun.Ghost);
            DailyRunTicket fourth = h.Service.BeginDailyRun();
            DailyRunOutcome wrongTrack = h.Apply(fourth, 9000, RecordLine(fourth.Seed + 1, 900.0, 0f));
            Assert.IsTrue(wrongTrack.NewDailyBest);
            Assert.IsFalse(wrongTrack.GhostRecorded);
            Assert.IsNull(h.Service.DailyRunGhost(fourth.Day));

            // Tomorrow: no ghost until tomorrow's first run.
            h.Utc = h.Utc.AddDays(1);
            Assert.IsNull(h.Service.BeginDailyRun().Ghost);
            Assert.IsNull(h.Service.DailyRunGhost(third.Day + 1));
        }

        [Test]
        public void Ghost_OfAnotherGeneratorVersion_IsIgnored()
        {
            var h = new Harness(Sep26.AddHours(11));
            DailyRunTicket t = h.Service.BeginDailyRun();
            h.Apply(t, 3000, RecordLine(t.Seed, 400.0, 0.2f));
            Assert.IsNotNull(h.Service.DailyRunGhost(t.Day));

            byte[] bytes = GhostPath.Decode(h.Service.Profile.DailyRun.Ghost).ToBytes();
            bytes[1] = (byte)(DailyRuns.GeneratorVersion + 1);
            h.Service.Profile.DailyRun.Ghost = Convert.ToBase64String(bytes);
            Assert.IsNull(h.Service.DailyRunGhost(t.Day));

            h.Service.Profile.DailyRun.Ghost = "@@not base64@@";
            Assert.IsNull(h.Service.DailyRunGhost(t.Day));
        }

        // ------------------------------------------------------------------ save migration

        [Test]
        public void Sanitize_MissingDailyRunAndGhostSetting_LoadAsFreshDailyRunAndGhostsOn()
        {
            // What JsonUtility produces for a save without the daily objects: no DailyRun object (worst case: null),
            // HideGhost absent (false).
            PlayerProfile p = PlayerProfile.CreateNew();
            p.Version = 1;
            p.Gems = 1234;
            p.Stats.Runs = 17;
            p.Flags.TutorialDone = true;
            p.DailyRun = null;
            p.Settings = new PlayerSettings { MusicVolume = 0.4f };

            p.Sanitize();
            Assert.AreEqual(1, p.Version);
            Assert.AreEqual(PlayerProfile.CurrentVersion, p.Version);
            Assert.IsNotNull(p.DailyRun);
            Assert.AreEqual(0, p.DailyRun.Day);
            Assert.AreEqual(0, p.DailyRun.Streak);
            Assert.AreEqual(string.Empty, p.DailyRun.Ghost);
            Assert.IsTrue(p.Settings.ShowGhost, "ghost defaults to on for old saves");
            Assert.AreEqual(1234, p.Gems, "existing data is kept");
            Assert.AreEqual(17, p.Stats.Runs);
            Assert.AreEqual(0.4f, p.Settings.MusicVolume);

            var service = new ProfileService(p, null, 5) { UtcClock = () => Sep26.AddHours(3) };
            Assert.IsTrue(service.DailyRunUnlocked);
            Assert.IsFalse(service.DailyRunPlayedToday);
            Assert.AreEqual(0, service.DailyRunStreak);
            Assert.IsNull(service.BeginDailyRun().Ghost);

            // Sanitize is idempotent.
            p.Sanitize();
            Assert.AreEqual(1, p.Version);
        }

        [Test]
        public void Sanitize_RepairsDailyRunGarbage()
        {
            PlayerProfile p = PlayerProfile.CreateNew();
            p.DailyRun = new DailyRunState
            {
                Day = 12, // 1970: impossible
                Streak = 40,
                BestScore = 7000,
                Ghost = "AAAA",
                Attempts = -2,
                TotalRuns = -1,
            };
            p.Sanitize();
            Assert.AreEqual(0, p.DailyRun.Day);
            Assert.AreEqual(0, p.DailyRun.Streak);
            Assert.AreEqual(0, p.DailyRun.BestScore);
            Assert.AreEqual(string.Empty, p.DailyRun.Ghost);
            Assert.AreEqual(0, p.DailyRun.Attempts);
            Assert.AreEqual(0, p.DailyRun.TotalRuns);

            int day = DailyRuns.DayIndex(Sep26);
            p.DailyRun = new DailyRunState
            {
                Day = day,
                Streak = 0,
                BestStreak = -4,
                BestScore = -10,
                BestDistance = float.NaN,
                BestShip = null,
                Ghost = new string('A', GhostPath.MaxEncodedChars + 4),
                AllTimeBestScore = 3,
            };
            p.Sanitize();
            Assert.AreEqual(day, p.DailyRun.Day);
            Assert.AreEqual(1, p.DailyRun.Streak, "a recorded day is a streak of at least one");
            Assert.AreEqual(1, p.DailyRun.BestStreak);
            Assert.AreEqual(0, p.DailyRun.BestScore);
            Assert.AreEqual(0f, p.DailyRun.BestDistance);
            Assert.AreEqual(string.Empty, p.DailyRun.BestShip);
            Assert.AreEqual(string.Empty, p.DailyRun.Ghost);
            Assert.AreEqual(3, p.DailyRun.AllTimeBestScore);

            // Damaged ghost text that fits the size limit stays until used, and is then ignored safely.
            p.DailyRun.Ghost = "!!!!";
            p.Sanitize();
            var service = new ProfileService(p, null, 5) { UtcClock = () => Sep26.AddHours(1) };
            Assert.IsNull(service.DailyRunGhost(day));
            Assert.IsNull(service.BeginDailyRun().Ghost);
        }

        [Test]
        public void NewerSave_KeepsDailyData()
        {
            PlayerProfile p = PlayerProfile.CreateNew();
            p.Version = 7;
            p.DailyRun.Day = DailyRuns.DayIndex(Sep26);
            p.DailyRun.Streak = 4;
            p.Sanitize();
            Assert.AreEqual(PlayerProfile.CurrentVersion, p.Version);
            Assert.AreEqual(4, p.DailyRun.Streak);
        }

        [Test]
        public void Merge_KeepsTheLaterDayAndTheBetterBest_AndNeverReopensARewardedDay()
        {
            int day = DailyRuns.DayIndex(Sep26);
            var a = new DailyRunState
            {
                Day = day, Streak = 3, BestStreak = 5, BestScore = 100, Ghost = "A", Attempts = 2, DaysPlayed = 9, TotalRuns = 20,
                AllTimeBestScore = 900,
            };
            var b = new DailyRunState
            {
                Day = day, Streak = 3, BestStreak = 4, BestScore = 300, BestShip = "manta", Ghost = "B", Attempts = 4, DaysPlayed = 8,
                TotalRuns = 25, AllTimeBestScore = 300,
            };
            DailyRunState m = DailyRunState.Merge(a, b);
            Assert.AreEqual(day, m.Day);
            Assert.AreEqual(300, m.BestScore);
            Assert.AreEqual("B", m.Ghost);
            Assert.AreEqual("manta", m.BestShip);
            Assert.AreEqual(4, m.Attempts);
            Assert.AreEqual(3, m.Streak);
            Assert.AreEqual(5, m.BestStreak);
            Assert.AreEqual(9, m.DaysPlayed);
            Assert.AreEqual(25, m.TotalRuns);
            Assert.AreEqual(900, m.AllTimeBestScore);
            Assert.AreNotSame(a, m);
            Assert.AreNotSame(b, m);

            var older = new DailyRunState { Day = day - 1, Streak = 7, BestStreak = 7, BestScore = 5000, Ghost = "C" };
            DailyRunState m2 = DailyRunState.Merge(older, a);
            Assert.AreEqual(day, m2.Day);
            Assert.AreEqual(100, m2.BestScore, "an older day's best never overrides today's");
            Assert.AreEqual("A", m2.Ghost);
            Assert.AreEqual(3, m2.Streak);
            Assert.AreEqual(7, m2.BestStreak);
            Assert.AreEqual(m2.Day, DailyRunState.Merge(a, older).Day, "order does not matter");
            Assert.IsNotNull(DailyRunState.Merge(null, null));
            Assert.AreEqual(day, DailyRunState.Merge(null, a).Day);

            // After merging, today (already rewarded on one device) pays nothing on the other.
            var h = new Harness(Sep26.AddHours(20));
            h.Service.Profile.DailyRun = DailyRunState.Merge(h.Service.Profile.DailyRun, a);
            DailyRunOutcome o = h.Play(50);
            Assert.IsTrue(o.Counted);
            Assert.AreEqual(0, o.TotalReward);
            Assert.AreEqual(3, o.Attempts);
        }

        [Test]
        public void ResetProgress_ClearsTheDailyRunButKeepsTheGhostSetting()
        {
            var h = new Harness(Sep26.AddHours(5));
            h.Play(1000);
            h.Service.Settings.ShowGhost = false;
            h.Service.ResetProgress();
            Assert.AreEqual(0, h.Service.Profile.DailyRun.Day);
            Assert.IsFalse(h.Service.DailyRunPlayedToday);
            Assert.IsFalse(h.Service.Settings.ShowGhost);
            Assert.IsTrue(h.Service.Settings.HideGhost);
        }

        // ------------------------------------------------------------------ helpers

        private static ProfileService NewService(DateTime utc)
        {
            PlayerProfile p = PlayerProfile.CreateNew();
            p.Flags.TutorialDone = true;
            return new ProfileService(p, () => utc, 11) { UtcClock = () => utc };
        }

        private static RunResult Result(long score, float distance)
        {
            return new RunResult
            {
                Score = score,
                Distance = distance,
                Gems = 7,
                GapsBridged = (int)(distance / 40f),
                MaxMultiplier = 1,
                Duration = distance / 50f,
            };
        }

        /// <summary>A short weaving recording on <paramref name="seed"/>'s field (content does not matter here).</summary>
        private static GhostPath RecordLine(int seed, double length, float slope)
        {
            var rec = new GhostRecorder(seed, DailyRuns.GeneratorVersion);
            for (double d = 0.0; d <= length; d += 0.75)
            {
                rec.Record(d, (float)Math.Sin(d * slope / 100.0) * 6f, (float)Math.Cos(d * slope / 70.0) * 4f, d / 40.0);
            }

            return rec.ToPath();
        }

        /// <summary>
        /// FNV-1a over what defines a field up to <paramref name="length"/> metres: every cell of the generated
        /// <see cref="TrackGrid"/> (with its level and wall flag) and every pickup (kind, lane, layer, position, value).
        /// </summary>
        private static uint TrackFingerprint(int seed, double length)
        {
            var grid = new TrackGrid();
            var pickups = new List<Pickup>();
            var generator = new TrackGenerator(seed, new Course(1), false, DifficultyModifiers.Standard, new BalanceConfig(),
                null, grid, pickups);
            long tiles = FieldSpec.TileOf(length);
            generator.GenerateUntil(tiles);
            Assert.AreEqual(0, grid.FirstTile);
            uint h = 2166136261u;
            for (long t = 0; t < tiles; t++)
            {
                TrackRow row = grid.Row(t);
                h = Mix(h, row.Level);
                h = Mix(h, row.IsWall ? 1 : 0);
                for (int i = 0; i < FieldSpec.CellsPerRow; i++)
                {
                    h = Mix(h, (int)row.Cells[i]);
                }
            }

            foreach (Pickup p in pickups)
            {
                if (p.Z >= length)
                {
                    continue;
                }

                h = Mix(h, (int)p.Kind);
                h = Mix(h, p.Lane);
                h = Mix(h, p.Layer);
                h = Mix(h, p.Value);
                h = Mix(h, Q(p.Z, 100.0));
                h = Mix(h, Q(p.X, 1000.0));
                h = Mix(h, Q(p.Y, 1000.0));
            }

            return h;
        }

        private static int Q(double value, double scale)
        {
            return (int)Math.Round(value * scale);
        }

        private static uint Mix(uint h, int value)
        {
            unchecked
            {
                for (int i = 0; i < 4; i++)
                {
                    h ^= (byte)(value >> (8 * i));
                    h *= 16777619u;
                }

                return h;
            }
        }

        private struct DailyState : IEquatable<DailyState>
        {
            public int Day;
            public int Streak;
            public long Best;
            public int Attempts;
            public string Ghost;
            public int Runs;

            public static DailyState Of(ProfileService s)
            {
                DailyRunState d = s.Profile.DailyRun;
                return new DailyState
                {
                    Day = d.Day,
                    Streak = d.Streak,
                    Best = d.BestScore,
                    Attempts = d.Attempts,
                    Ghost = d.Ghost,
                    Runs = d.TotalRuns,
                };
            }

            public bool Equals(DailyState o)
            {
                return Day == o.Day && Streak == o.Streak && Best == o.Best && Attempts == o.Attempts && Ghost == o.Ghost && Runs == o.Runs;
            }

            public override bool Equals(object obj)
            {
                return obj is DailyState other && Equals(other);
            }

            public override int GetHashCode()
            {
                return Day ^ (Streak << 8) ^ Attempts;
            }

            public override string ToString()
            {
                return "day " + Day + " streak " + Streak + " best " + Best + " attempts " + Attempts + " runs " + Runs;
            }
        }

        /// <summary>A profile whose UTC clock the test moves; plays complete Daily Runs.</summary>
        private sealed class Harness
        {
            public readonly ProfileService Service;
            public readonly List<DailyRunOutcome> Events = new List<DailyRunOutcome>();
            public DateTime Utc;
            public long LastDailyGems;
            public long RunGems;

            public Harness(DateTime utc)
            {
                Utc = utc;
                PlayerProfile p = PlayerProfile.CreateNew();
                p.Flags.TutorialDone = true;
                Service = new ProfileService(p, () => Utc, 21);
                Service.UtcClock = () => Utc;
                Service.DailyRunCompleted += Events.Add;
            }

            public DailyRunOutcome Play(long score, float distance = 800f)
            {
                return Apply(Service.BeginDailyRun(), score, null, distance);
            }

            public DailyRunOutcome Apply(DailyRunTicket ticket, long score, GhostPath ghost, float distance = 800f)
            {
                long beforeRun = Service.Gems;
                RunSummary summary = Service.ApplyRun(Result(score, distance));
                RunGems += Service.Gems - beforeRun;
                long before = Service.Gems;
                DailyRunOutcome o = Service.ApplyDailyRun(summary, ticket, ghost);
                LastDailyGems = Service.Gems - before;
                return o;
            }
        }
    }
}

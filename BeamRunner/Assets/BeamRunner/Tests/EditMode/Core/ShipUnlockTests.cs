using System;
using System.Collections.Generic;
using BeamRunner.Core.Meta;
using BeamRunner.Core.Sim;
using NUnit.Framework;

namespace BeamRunner.Core.Tests
{
    /// <summary>
    /// Ship unlocks: requirement gates (rank, score, Daily Run days) followed by a gem price, and the pacing rules the
    /// catalog must keep — no player can own the whole hangar within days, however much they play or buy.
    /// </summary>
    public class ShipUnlockTests
    {
        private static readonly DateTime Start = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

        [SetUp]
        public void InstallEnglishStrings()
        {
            LocTestSupport.InstallEnglish();
        }

        private static ProfileService NewService(PlayerProfile profile, Func<DateTime> utc)
        {
            var s = new ProfileService(profile ?? PlayerProfile.CreateNew(), () => utc(), 5);
            s.UtcClock = utc;
            return s;
        }

        private static RunResult Run(long score, float distance = 2000f, int gems = 0)
        {
            return new RunResult
            {
                Score = score,
                Distance = distance,
                Gems = gems,
                GapsBridged = (int)(distance / 40f),
                MaxMultiplier = 2,
                Duration = distance / 50f,
            };
        }

        /// <summary>A normal run followed by the day's first counted Daily Run, like GameController.FinishRun.</summary>
        private static RunSummary FlyDailyRun(ProfileService s)
        {
            DailyRunTicket ticket = s.BeginDailyRun();
            RunSummary summary = s.ApplyRun(Run(1000));
            DailyRunOutcome o = s.ApplyDailyRun(summary, ticket, null);
            Assert.IsTrue(o.Counted);
            return summary;
        }

        private static ShipDefinition Ship(string id)
        {
            return ShipCatalog.Get(id);
        }

        // ------------------------------------------------------------------ catalog contract

        [Test]
        public void EveryLockedShip_CostsGems_AndPricesClimbThroughTheHangar()
        {
            IReadOnlyList<ShipDefinition> all = ShipCatalog.All;
            int previous = 0;
            for (int i = 0; i < all.Count; i++)
            {
                ShipDefinition ship = all[i];
                if (ship.Unlock == UnlockKind.Free)
                {
                    Assert.AreEqual(0, ship.Price, ship.Id);
                    continue;
                }

                Assert.That(ship.Price, Is.GreaterThan(previous), ship.Id + ": each ship costs more than the one before");
                Assert.That(ship.HasRequirement ? ship.UnlockValue : 1, Is.GreaterThan(0), ship.Id);
                previous = ship.Price;
            }
        }

        [Test]
        public void EveryShip_HasAUniqueIdAndLivery_OnAnExistingHull()
        {
            var ids = new HashSet<string>();
            var liveries = new HashSet<(ColorRGB, ColorRGB)>();
            var hulls = new HashSet<string> { "arrow", "manta", "viper", "halo", "phantom", "nova", "shuttle" };
            ShipDefinition flagship = ShipCatalog.All[ShipCatalog.All.Count - 1];
            foreach (ShipDefinition ship in ShipCatalog.All)
            {
                Assert.IsTrue(ids.Add(ship.Id), "duplicate id " + ship.Id);
                Assert.IsTrue(liveries.Add((ship.Body, ship.Glow)), ship.Id + " looks like another ship");
                Assert.IsTrue(hulls.Contains(ship.Design), ship.Id + ": unknown hull " + ship.Design);
                Assert.AreEqual("ship." + ship.Id, ship.NameKey);
                if (ship != flagship)
                {
                    Assert.AreNotEqual(flagship.Design, ship.Design, ship.Id + ": the flagship's hull is its own");
                }
            }
        }

        [Test]
        public void TheFlagship_NeedsAMonthOfDailyRuns_AndAboutAMonthOfGems()
        {
            ShipDefinition last = ShipCatalog.All[ShipCatalog.All.Count - 1];
            Assert.AreEqual(UnlockKind.DailyRunDays, last.Unlock);
            Assert.That(last.UnlockValue, Is.GreaterThanOrEqualTo(30), "a month of Daily Runs");

            // ~10 minutes a day for a month earns roughly 35k (casual) to 50k (average) gems without ads; see
            // docs/ARCHITECTURE.md "Ship unlock pacing". The flagship alone costs about that month.
            Assert.That(last.Price, Is.GreaterThanOrEqualTo(35000));

            long total = 0;
            foreach (ShipDefinition ship in ShipCatalog.All)
            {
                total += ship.Price;
            }

            Assert.That(total, Is.GreaterThanOrEqualTo(100000), "the whole hangar is about two months of gems");
        }

        [Test]
        public void OneHugeDay_CannotUnlockTheDailyRunShips()
        {
            // Everything a player could possibly reach on day one: a maxed wallet (IAP), rank, score and a Daily Run.
            PlayerProfile p = PlayerProfile.CreateNew();
            p.PilotRank = 50;
            p.Gems = 10000000;
            ProfileService s = NewService(p, () => Start);
            s.ApplyRun(Run(10000000, 50000f));
            FlyDailyRun(s);

            foreach (ShipDefinition ship in ShipCatalog.All)
            {
                ShipPurchaseResult r = s.BuyShip(ship.Id);
                if (ship.Unlock == UnlockKind.DailyRunDays)
                {
                    Assert.AreEqual(ShipPurchaseResult.Locked, r, ship.Id + " can't be bought on day one");
                }
            }

            Assert.IsFalse(s.Owns("halo"));
            Assert.IsFalse(s.Owns("nova"));
        }

        // ------------------------------------------------------------------ gates

        [Test]
        public void ScoreGate_ReportsTheShipOnce_ThenItIsBoughtForItsPrice()
        {
            ShipDefinition viper = Ship("viper");
            ProfileService s = NewService(null, () => Start);
            s.AddGems(viper.Price, "test");

            Assert.AreEqual(ShipPurchaseResult.Locked, s.BuyShip("viper"), "gems alone are not enough");
            Assert.IsFalse(s.CanUnlockNow(viper));
            Assert.AreEqual(0f, s.UnlockProgress(viper));

            RunSummary close = s.ApplyRun(Run(viper.UnlockValue - 1));
            CollectionAssert.IsEmpty(close.ShipsAvailable);
            Assert.That(s.UnlockProgress(viper), Is.GreaterThan(0.99f).And.LessThan(1f));

            RunSummary met = s.ApplyRun(Run(viper.UnlockValue));
            CollectionAssert.AreEqual(new[] { viper }, met.ShipsAvailable);
            CollectionAssert.IsEmpty(met.ShipsUnlocked, "a priced ship is never given away");
            Assert.IsFalse(s.Owns("viper"));
            Assert.IsTrue(s.CanUnlockNow(viper));

            RunSummary again = s.ApplyRun(Run(viper.UnlockValue + 1L));
            CollectionAssert.DoesNotContain(again.ShipsAvailable, viper, "reported only when first met");

            long before = s.Gems;
            Assert.AreEqual(ShipPurchaseResult.Purchased, s.BuyShip("viper"));
            Assert.AreEqual(before - viper.Price + PaidAchievements(s), s.Gems);
        }

        [Test]
        public void ScoreGate_MetWithoutTheGems_SaysNotEnoughGems()
        {
            ProfileService s = NewService(null, () => Start);
            s.ApplyRun(Run(Ship("viper").UnlockValue));
            Assert.IsTrue(s.RequirementMet(Ship("viper")));
            Assert.AreEqual(ShipPurchaseResult.NotEnoughGems, s.BuyShip("viper"));
            Assert.IsFalse(s.Owns("viper"));
        }

        [Test]
        public void LevelGate_OpensWhenARunReachesTheLevel()
        {
            ShipDefinition spark = Ship("spark");
            Assert.AreEqual(UnlockKind.Level, spark.Unlock);
            ProfileService s = NewService(null, () => Start);
            Assert.AreEqual(1, s.RequirementCurrent(spark), "a fresh pilot shows LEVEL 1 / n, not 0");
            s.AddGems(spark.Price, "test");
            s.ApplyRun(new RunResult { Score = 100, Distance = 500f, StartLevel = 1, LevelReached = (int)spark.UnlockValue - 1 });
            Assert.AreEqual(ShipPurchaseResult.Locked, s.BuyShip("spark"));
            Assert.AreEqual(spark.UnlockValue - 1, s.RequirementCurrent(spark));

            RunSummary met = s.ApplyRun(new RunResult { Score = 100, Distance = 500f, StartLevel = 1, LevelReached = (int)spark.UnlockValue });
            CollectionAssert.Contains(met.ShipsAvailable, spark);
            Assert.AreEqual(1f, s.UnlockProgress(spark));
            Assert.AreEqual(ShipPurchaseResult.Purchased, s.BuyShip("spark"));
        }

        [Test]
        public void RankGate_OpensAtTheRank()
        {
            ShipDefinition phantom = Ship("phantom");
            PlayerProfile p = PlayerProfile.CreateNew();
            p.Gems = phantom.Price;
            p.PilotRank = phantom.UnlockValue - 1;
            ProfileService s = NewService(p, () => Start);
            Assert.AreEqual(ShipPurchaseResult.Locked, s.BuyShip("phantom"));
            Assert.AreEqual(phantom.UnlockValue - 1, s.RequirementCurrent(phantom));

            p.PilotRank = phantom.UnlockValue;
            Assert.AreEqual(ShipPurchaseResult.Purchased, s.BuyShip("phantom"));
        }

        [Test]
        public void DailyRunDaysGate_CountsDistinctDays_NotRunsOrStreaks()
        {
            ShipDefinition halo = Ship("halo");
            DateTime now = Start;
            PlayerProfile p = PlayerProfile.CreateNew();
            p.Flags.TutorialDone = true;
            p.Gems = 10000000;
            ProfileService s = NewService(p, () => now);

            // Many Daily Runs on one day count once.
            for (int i = 0; i < 5; i++)
            {
                DailyRunTicket t = s.BeginDailyRun();
                s.ApplyDailyRun(s.ApplyRun(Run(1000)), t, null);
            }

            Assert.AreEqual(1, s.RequirementCurrent(halo));

            // Days with a gap still count (days flown, not a streak).
            int day = 1;
            while (day < halo.UnlockValue - 1)
            {
                now = now.AddDays(2);
                FlyDailyRun(s);
                day++;
            }

            Assert.AreEqual(halo.UnlockValue - 1, s.RequirementCurrent(halo));
            Assert.AreEqual(ShipPurchaseResult.Locked, s.BuyShip("halo"));

            now = now.AddDays(1);
            RunSummary last = FlyDailyRun(s);
            CollectionAssert.Contains(last.ShipsAvailable, halo, "the Daily Run that meets the requirement reports it");
            Assert.AreEqual(1f, s.UnlockProgress(halo));
            Assert.AreEqual(ShipPurchaseResult.Purchased, s.BuyShip("halo"));
        }

        [Test]
        public void Flagship_StaysLockedUntilTheThirtiethDailyRunDay()
        {
            ShipDefinition nova = Ship("nova");
            DateTime now = Start;
            PlayerProfile p = PlayerProfile.CreateNew();
            p.Flags.TutorialDone = true;
            ProfileService s = NewService(p, () => now);
            for (int day = 1; day < nova.UnlockValue; day++)
            {
                FlyDailyRun(s);
                now = now.AddDays(1);
            }

            s.AddGems(nova.Price, "test");
            Assert.AreEqual(ShipPurchaseResult.Locked, s.BuyShip("nova"), "day " + (nova.UnlockValue - 1));

            FlyDailyRun(s);
            Assert.AreEqual(ShipPurchaseResult.Purchased, s.BuyShip("nova"));
        }

        [Test]
        public void ShipsOwnedInOlderSaves_StayOwned_WhateverTheNewRequirements()
        {
            // A v2 save from before the rebalance: Halo came free at rank 8, Nova with a 10 km run.
            var old = new PlayerProfile
            {
                PilotRank = 8,
                OwnedShips = new List<string> { "arrow", "shuttle", "manta", "halo", "nova" },
                SelectedShip = "nova",
            };

            ProfileService s = NewService(old, () => Start);
            Assert.IsTrue(s.Owns("halo"));
            Assert.IsTrue(s.Owns("nova"));
            Assert.AreEqual("nova", s.SelectedShip.Id);
            Assert.AreEqual(ShipPurchaseResult.AlreadyOwned, s.BuyShip("nova"));
        }

        [Test]
        public void UnlockDescriptions_NameTheRequirement()
        {
            StringAssert.Contains("25,000", Ship("viper").UnlockDescription);
            StringAssert.Contains("LEVEL 4", Ship("spark").UnlockDescription);
            StringAssert.Contains("DAILY RUN", Ship("halo").UnlockDescription);
            StringAssert.Contains("30", Ship("nova").UnlockDescription);
            StringAssert.Contains("RANK", Ship("phantom").UnlockDescription);
            StringAssert.Contains("GEMS", Ship("manta").UnlockDescription);
        }

        private static long PaidAchievements(ProfileService s)
        {
            long reward = 0;
            foreach (AchievementDefinition a in s.LastPurchaseAchievements)
            {
                reward += a.Reward;
            }

            return reward;
        }
    }
}

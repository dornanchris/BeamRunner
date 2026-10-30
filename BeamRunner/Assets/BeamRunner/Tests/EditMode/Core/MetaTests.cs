using System;
using BeamRunner.Core.Field;
using BeamRunner.Core.Meta;
using BeamRunner.Core.Sim;
using NUnit.Framework;

namespace BeamRunner.Core.Tests
{
    public class MetaTests
    {
        private static DateTime _now = new DateTime(2026, 3, 10, 12, 0, 0);

        // Reminder / mission texts come from the shipped English string table (Core/Localization).
        [SetUp]
        public void InstallEnglishStrings()
        {
            LocTestSupport.InstallEnglish();
        }

        private static ProfileService NewService(PlayerProfile profile = null)
        {
            return new ProfileService(profile ?? PlayerProfile.CreateNew(), () => _now, 99);
        }

        private static RunResult Run(long score, float distance, int gems = 10, int nearMisses = 3)
        {
            return new RunResult
            {
                Score = score,
                Distance = distance,
                Gems = gems,
                NearMisses = nearMisses,
                GapsBridged = (int)(distance / 40f),
                MaxMultiplier = 2,
                Duration = distance / 50f,
            };
        }

        [Test]
        public void NewProfile_HasStarterShipsAndMissions()
        {
            ProfileService s = NewService();
            Assert.IsTrue(s.Owns("arrow"));
            Assert.IsTrue(s.Owns("shuttle"));
            Assert.IsFalse(s.Owns("manta"));
            Assert.AreEqual(MissionSystem.ActiveCount, s.Profile.Missions.Active.Count);
            Assert.AreEqual("arrow", s.SelectedShip.Id);
        }

        [Test]
        public void Sanitize_RepairsGarbage()
        {
            var p = new PlayerProfile
            {
                Gems = -50,
                PilotRank = 0,
                SelectedShip = "does-not-exist",
                OwnedShips = null,
                Stats = null,
                Settings = new PlayerSettings { MusicVolume = 7f, Control = 42, Sensitivity = 100f },
                Leaderboard = null,
                Missions = null,
            };
            p.Sanitize();
            Assert.AreEqual(0, p.Gems);
            Assert.AreEqual(1, p.PilotRank);
            Assert.AreEqual(ShipCatalog.DefaultShipId, p.SelectedShip);
            Assert.IsTrue(p.OwnedShips.Contains("arrow"));
            Assert.IsNotNull(p.Stats);
            Assert.AreEqual(1f, p.Settings.MusicVolume);
            Assert.AreEqual((int)ControlMode.Swipe, p.Settings.Control);
            Assert.IsNotNull(p.Leaderboard);
            Assert.IsNotNull(p.Missions.Active);

            // A tampered level must not let a run start somewhere Levels.Get would take forever to reach.
            var levels = new PlayerProfile
            {
                Stats = new PlayerStats { BestLevel = int.MaxValue },
                Settings = new PlayerSettings { StartLevel = int.MaxValue },
            };
            levels.Sanitize();
            Assert.AreEqual(Levels.MaxLevel, levels.Stats.BestLevel);
            Assert.AreEqual(Levels.MaxLevel, levels.Settings.StartLevel);
            Assert.AreEqual(Levels.MaxLevel, Levels.Get(int.MaxValue).Number);
        }

        [Test]
        public void ApplyRun_UpdatesBestsWalletAndLeaderboard()
        {
            ProfileService s = NewService();
            RunSummary first = s.ApplyRun(Run(1000, 500, 20));
            Assert.IsTrue(first.NewBestScore);
            Assert.AreEqual(0, first.LeaderboardPosition);
            Assert.That(s.Gems, Is.GreaterThanOrEqualTo(20));

            RunSummary worse = s.ApplyRun(Run(400, 200, 5));
            Assert.IsFalse(worse.NewBestScore);
            Assert.AreEqual(1, worse.LeaderboardPosition);
            Assert.AreEqual(1000, s.Profile.Stats.BestScore);
            Assert.AreEqual(2, s.Profile.Stats.Runs);

            for (int i = 0; i < 15; i++)
            {
                s.ApplyRun(Run(2000 + i, 900));
            }

            Assert.AreEqual(ProfileService.LeaderboardSize, s.Profile.Leaderboard.Count);
            for (int i = 1; i < s.Profile.Leaderboard.Count; i++)
            {
                Assert.That(s.Profile.Leaderboard[i - 1].Score, Is.GreaterThanOrEqualTo(s.Profile.Leaderboard[i].Score));
            }

            Assert.AreEqual(-1, s.ApplyRun(Run(10, 20)).LeaderboardPosition);
        }

        [Test]
        public void CompletingAMissionSet_RanksUpAndRefills()
        {
            ProfileService s = NewService();
            RunResult huge = new RunResult
            {
                Score = 10000000,
                Distance = 50000,
                Gems = 5000,
                NearMisses = 500,
                BestNearMissChain = 100,
                GapsBridged = 5000,
                CubesDodged = 5000,
                Smashed = 500,
                Overloads = 50,
                MaxMultiplier = 8,
                StartLevel = 1,
                LevelReached = 30,
                LevelsCleared = 29,
                PowerUps = 100,
                Builds = 5000,
                Bridges = 4000,
                RampsUp = 500,
                RampsDown = 500,
            };

            RunSummary summary = s.ApplyRun(huge);
            Assert.AreEqual(3, summary.MissionsCompleted.Count);
            Assert.IsTrue(summary.RankUp);
            Assert.AreEqual(2, s.PilotRank);
            Assert.AreEqual(MissionSystem.ActiveCount, s.Profile.Missions.Active.Count);
            foreach (Mission m in s.Profile.Missions.Active)
            {
                Assert.IsFalse(m.Completed);
            }

            Assert.That(summary.AchievementsUnlocked.Count, Is.GreaterThan(5));
            Assert.IsFalse(s.Owns("viper"), "a priced ship is never given away by a run");
            CollectionAssert.Contains(summary.ShipsAvailable, ShipCatalog.Get("viper"), "the huge score met the Viper's requirement");
            CollectionAssert.DoesNotContain(summary.ShipsAvailable, ShipCatalog.Get("nova"), "Daily Run days are not met by one run");
            Assert.That(s.ScoreBonus, Is.EqualTo(ProfileService.ScoreBonusPerRank).Within(1e-5f));
        }

        [Test]
        public void MissionText_AndNiceRounding()
        {
            Assert.AreEqual(1200, MissionSystem.RoundNice(1234));
            Assert.AreEqual(85, MissionSystem.RoundNice(87));
            Assert.AreEqual(6, MissionSystem.RoundNice(6));
            Assert.AreEqual(150, MissionSystem.RoundNice(150));
            var m = new Mission { Type = (int)MissionType.DistanceRun, Target = 800 };
            StringAssert.Contains("800", MissionSystem.Describe(m));
        }

        [Test]
        public void ShipPurchase_RespectsWalletAndLocks()
        {
            ProfileService s = NewService();
            int price = ShipCatalog.Get("manta").Price;
            s.AddGems(price - 1, "test");
            Assert.AreEqual(ShipPurchaseResult.NotEnoughGems, s.BuyShip("manta"));
            s.AddGems(251, "test");
            Assert.AreEqual(ShipPurchaseResult.Purchased, s.BuyShip("manta"));
            Assert.AreEqual(250, s.Gems);
            Assert.AreEqual(ShipPurchaseResult.AlreadyOwned, s.BuyShip("manta"));
            Assert.AreEqual(ShipPurchaseResult.Locked, s.BuyShip("halo"));
            Assert.AreEqual(ShipPurchaseResult.UnknownShip, s.BuyShip("zzz"));
            Assert.IsTrue(s.SelectShip("manta"));
            Assert.IsFalse(s.SelectShip("viper"));
        }

        [Test]
        public void DailyRewards_StreakAdvancesAndResets()
        {
            var state = new DailyRewardState();
            DateTime day = new DateTime(2026, 1, 1, 9, 0, 0);
            Assert.AreEqual(DailyRewards.Rewards[0], DailyRewards.Claim(state, day));
            Assert.AreEqual(0, DailyRewards.Claim(state, day.AddHours(10)), "once per day");
            Assert.AreEqual(DailyRewards.Rewards[1], DailyRewards.Claim(state, day.AddDays(1)));
            Assert.AreEqual(DailyRewards.Rewards[2], DailyRewards.Claim(state, day.AddDays(2)));

            // Skip a day → back to day 1.
            Assert.AreEqual(DailyRewards.Rewards[0], DailyRewards.Claim(state, day.AddDays(4)));

            // Full week then wrap.
            DateTime start = day.AddDays(10);
            for (int i = 0; i < 7; i++)
            {
                Assert.AreEqual(DailyRewards.Rewards[i], DailyRewards.Claim(state, start.AddDays(i)));
            }

            Assert.AreEqual(DailyRewards.Rewards[0], DailyRewards.Claim(state, start.AddDays(7)));
        }

        [Test]
        public void Interstitials_AreRateLimited()
        {
            ProfileService s = NewService();
            DateTime utc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            for (int i = 0; i < 3; i++)
            {
                s.ApplyRun(Run(100, 100));
                Assert.IsFalse(s.ShouldShowInterstitial(utc), "never in the first runs");
            }

            s.ApplyRun(Run(100, 100));
            Assert.IsTrue(s.ShouldShowInterstitial(utc));
            s.MarkInterstitialShown(utc);
            s.ApplyRun(Run(100, 100));
            s.ApplyRun(Run(100, 100));
            s.ApplyRun(Run(100, 100));
            Assert.IsFalse(s.ShouldShowInterstitial(utc.AddSeconds(30)), "time-limited");
            Assert.IsTrue(s.ShouldShowInterstitial(utc.AddSeconds(300)));
            s.GrantEntitlement("beamrunner.removeads", true, 0);
            Assert.IsFalse(s.ShouldShowInterstitial(utc.AddSeconds(300)), "remove ads");
        }

        [Test]
        public void DoubleGems_OnlyOnce()
        {
            ProfileService s = NewService();
            RunSummary summary = s.ApplyRun(Run(100, 100, 30));
            long before = s.Gems;
            Assert.AreEqual(30, s.DoubleRunGems(summary));
            Assert.AreEqual(0, s.DoubleRunGems(summary));
            Assert.AreEqual(before + 30, s.Gems);
        }

        [Test]
        public void ResetProgress_KeepsPurchasesAndSettings()
        {
            ProfileService s = NewService();
            s.Settings.MusicVolume = 0.3f;
            s.GrantEntitlement("beamrunner.removeads", true, 500);
            s.ApplyRun(Run(5000, 2000));
            s.ResetProgress();
            Assert.AreEqual(0, s.Gems);
            Assert.AreEqual(0, s.Profile.Stats.Runs);
            Assert.IsTrue(s.AdsRemoved);
            Assert.AreEqual(0.3f, s.Settings.MusicVolume);
        }

        [Test]
        public void Achievements_UnlockOnceWithoutGrantingTwice()
        {
            ProfileService s = NewService();
            RunSummary a = s.ApplyRun(Run(100, 1200));
            Assert.IsTrue(a.AchievementsUnlocked.Exists(x => x.Id == "first_flight"));
            Assert.IsTrue(a.AchievementsUnlocked.Exists(x => x.Id == "km_1"));
            RunSummary b = s.ApplyRun(Run(100, 1300));
            Assert.IsFalse(b.AchievementsUnlocked.Exists(x => x.Id == "km_1"));
        }

        [Test]
        public void BuyShip_PaysTheAchievementsItUnlocks_ExactlyOnce()
        {
            PlayerProfile p = PlayerProfile.CreateNew();
            p.PilotRank = ShipCatalog.Get("phantom").UnlockValue;
            p.Gems = 100000;
            ProfileService s = NewService(p);
            s.ApplyRun(Run(100, 500));
            Assert.AreEqual(ShipPurchaseResult.Purchased, s.BuyShip("manta")); // 3 ships owned
            long before = s.Profile.Gems;

            Assert.AreEqual(ShipPurchaseResult.Purchased, s.BuyShip("phantom")); // 4th ship: HANGAR QUEEN
            AchievementDefinition fleet = s.LastPurchaseAchievements.Find(x => x.Id == "fleet_4");
            Assert.IsNotNull(fleet, "the purchase must report the achievement it unlocked");
            long price = ShipCatalog.Get("phantom").Price;
            Assert.AreEqual(before - price + fleet.Reward, s.Profile.Gems, "reward paid with the purchase");

            RunSummary next = s.ApplyRun(Run(100, 500));
            Assert.IsFalse(next.AchievementsUnlocked.Exists(x => x.Id == "fleet_4"), "and never again after a run");
        }

        [Test]
        public void Missions_PerRunTargetsAreCappedAtEveryRank_AndStoredOnesAreClamped()
        {
            for (int rank = 1; rank <= 60; rank++)
            {
                var state = new MissionState();
                // Many draws per rank so every template shows up.
                for (int seed = 1; seed <= 40; seed++)
                {
                    state.Active.Clear();
                    MissionSystem.EnsureMissions(state, rank, new Rng(seed * 7919 + rank));
                    foreach (Mission m in state.Active)
                    {
                        Assert.That(m.Target, Is.LessThanOrEqualTo(MissionSystem.MaxTargetFor(m.MissionType)),
                            m.MissionType + " at rank " + rank);
                    }
                }
            }

            // Every "in one run" mission is capped (the caps are tuning; their values live in Missions.cs).
            foreach (MissionType type in new[]
                     {
                         MissionType.GemsRun, MissionType.DistanceRun, MissionType.NearMissesRun, MissionType.OverloadsRun,
                         MissionType.CubesDodgedRun, MissionType.LevelsClearedRun, MissionType.ScoreRun, MissionType.MultiplierRun,
                         MissionType.ChainRun, MissionType.BridgesRun, MissionType.BuildsRun,
                     })
            {
                Assert.That(MissionSystem.MaxTargetFor(type), Is.LessThan(long.MaxValue), type + " is capped");
            }

            Assert.That(MissionSystem.MaxTargetFor(MissionType.LevelsClearedRun), Is.InRange(3, 12), "about the levels in a long run");
            Assert.AreEqual(RunSimulation.MaxMultiplier, MissionSystem.MaxTargetFor(MissionType.MultiplierRun));
            Assert.AreEqual(long.MaxValue, MissionSystem.MaxTargetFor(MissionType.DistanceTotal), "totals stay uncapped");
            Assert.AreEqual(long.MaxValue, MissionSystem.MaxTargetFor(MissionType.BridgesTotal));

            // A save from before the caps with an impossible mission is repaired on load.
            long levelsCap = MissionSystem.MaxTargetFor(MissionType.LevelsClearedRun);
            long dodgeCap = MissionSystem.MaxTargetFor(MissionType.CubesDodgedRun);
            var old = new MissionState();
            old.Active.Add(new Mission { Type = (int)MissionType.LevelsClearedRun, Target = levelsCap + 4, Reward = 200 });
            old.Active.Add(new Mission { Type = (int)MissionType.CubesDodgedRun, Target = dodgeCap * 3 + 7, Reward = 400 });
            MissionSystem.EnsureMissions(old, 30, new Rng(5));
            Assert.AreEqual(levelsCap, old.Active[0].Target);
            Assert.AreEqual(dodgeCap, old.Active[1].Target);
        }
    }
}

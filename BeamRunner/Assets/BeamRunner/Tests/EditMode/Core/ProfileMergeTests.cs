using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BeamRunner.Core.Meta;
using BeamRunner.Core.Sim;
using NUnit.Framework;

namespace BeamRunner.Core.Tests
{
    /// <summary>Cloud-save merge rules (<see cref="ProfileMerge"/>) and the gem ledger (<see cref="WalletLedger"/>).</summary>
    public class ProfileMergeTests
    {
        private static readonly DateTime Now = new DateTime(2026, 9, 20, 12, 0, 0);

        // ------------------------------------------------------------------ helpers

        private static PlayerProfile NewProfile(string device, long gems = 0)
        {
            PlayerProfile p = PlayerProfile.CreateNew();
            Service(p); // missions + first-launch time, like a real boot
            p.Wallet.DeviceId = device;
            p.Gems = gems;
            WalletLedger.Reconcile(p);
            return p;
        }

        private static ProfileService Service(PlayerProfile p)
        {
            return new ProfileService(p, () => Now, 7);
        }

        private static long Price(string shipId)
        {
            return ShipCatalog.Get(shipId).Price;
        }

        /// <summary>Meets the Viper's score requirement (its price still has to be paid).</summary>
        private static void MeetViperRequirement(PlayerProfile p)
        {
            p.Stats.BestScore = Math.Max(p.Stats.BestScore, ShipCatalog.Get("viper").UnlockValue);
        }

        private static RunResult Run(long score, float distance, int gems)
        {
            return new RunResult
            {
                Score = score,
                Distance = distance,
                Gems = gems,
                NearMisses = 4,
                GapsBridged = (int)(distance / 40f),
                MaxMultiplier = 3,
                Duration = distance / 50f,
                CubesDodged = (int)(distance / 25f),
                LevelReached = 2,
                LevelsCleared = 1,
            };
        }

        /// <summary>A copy of the snapshot as another device would have it (the save file was synced to it).</summary>
        private static PlayerProfile OnOtherDevice(PlayerProfile p, string device)
        {
            PlayerProfile copy = Clone(p);
            copy.Wallet.DeviceId = device;
            return copy;
        }

        /// <summary>Merge like a sync does: into the live local profile, from a fresh copy of the remote one.</summary>
        private static ProfileMergeReport Merge(PlayerProfile local, PlayerProfile remote)
        {
            return ProfileMerge.MergeInto(local, Clone(remote));
        }

        // ------------------------------------------------------------------ basics

        [Test]
        public void MergingWithItself_ChangesNothing()
        {
            PlayerProfile a = NewProfile("dev-a", 300);
            ProfileService s = Service(a);
            s.ApplyRun(Run(5000, 1200, 40));
            s.ApplyRun(Run(8000, 2100, 60));
            WalletLedger.Reconcile(a);
            PlayerProfile before = Clone(a);

            Merge(a, before);
            AssertDeepEqual(before, a);
            Merge(a, OnOtherDevice(before, "dev-b"));
            AssertDeepEqual(before, a);
        }

        [Test]
        public void LocalProfileWithoutDeviceId_IsRejected()
        {
            PlayerProfile a = PlayerProfile.CreateNew();
            Assert.Throws<ArgumentException>(() => ProfileMerge.MergeInto(a, PlayerProfile.CreateNew()));
        }

        [Test]
        public void NullRemote_IsANoOp()
        {
            PlayerProfile a = NewProfile("dev-a", 10);
            ProfileMergeReport r = ProfileMerge.MergeInto(a, null);
            Assert.AreEqual(10, a.Gems);
            Assert.AreEqual(10, r.GemsAfter);
        }

        [Test]
        public void Purchases_AreNeverLostToAnOlderSnapshot()
        {
            PlayerProfile a = NewProfile("dev-a");
            PlayerProfile cloud = OnOtherDevice(a, "dev-b");
            a.Flags.AdsRemoved = true;

            ProfileMergeReport r = Merge(a, cloud);
            Assert.IsTrue(a.Flags.AdsRemoved);
            Assert.IsFalse(r.GainedAdsRemoved);

            ProfileMergeReport r2 = Merge(cloud, a);
            Assert.IsTrue(cloud.Flags.AdsRemoved, "the other device learns about the purchase");
            Assert.IsTrue(r2.GainedAdsRemoved);
        }

        [Test]
        public void ShipsAndAchievements_AreUnited()
        {
            PlayerProfile a = NewProfile("dev-a", Price("manta") + Price("viper"));
            PlayerProfile b = OnOtherDevice(a, "dev-b");
            Assert.AreEqual(ShipPurchaseResult.Purchased, Service(a).BuyShip("manta"));
            MeetViperRequirement(b);
            Assert.AreEqual(ShipPurchaseResult.Purchased, Service(b).BuyShip("viper"));
            a.Achievements.Add("close_10");
            b.Achievements.Add("od_1");

            ProfileMergeReport r = Merge(a, b);
            CollectionAssert.Contains(a.OwnedShips, "manta");
            CollectionAssert.Contains(a.OwnedShips, "viper");
            CollectionAssert.Contains(a.Achievements, "close_10");
            CollectionAssert.Contains(a.Achievements, "od_1");
            CollectionAssert.AreEqual(new[] { "viper" }, r.NewShips);
            CollectionAssert.Contains(r.NewAchievements, "od_1");
        }

        [Test]
        public void Stats_TakeTheMaximumOfBestsAndTotals()
        {
            PlayerProfile a = NewProfile("dev-a");
            PlayerProfile b = OnOtherDevice(a, "dev-b");
            Service(a).ApplyRun(Run(9000, 3000, 10));
            Service(b).ApplyRun(Run(4000, 1000, 10));
            Service(b).ApplyRun(Run(3000, 900, 10));

            Merge(a, b);
            Assert.AreEqual(9000, a.Stats.BestScore);
            Assert.AreEqual(3000f, a.Stats.BestDistance);
            Assert.AreEqual(2, a.Stats.Runs, "totals: max, never the sum of shared history");
            Assert.AreEqual(Math.Max(3000.0, 1900.0), a.Stats.TotalDistance);
        }

        // ------------------------------------------------------------------ gems

        [Test]
        public void Gems_SpentOnOneDevice_AreNotResurrectedByTheOther()
        {
            // Both devices start from the same synced balance. A buys a ship; B still shows the full balance.
            PlayerProfile a = NewProfile("dev-a", Price("manta") + 1250);
            PlayerProfile b = OnOtherDevice(a, "dev-b");
            Assert.AreEqual(ShipPurchaseResult.Purchased, Service(a).BuyShip("manta"));
            Assert.AreEqual(1250, a.Gems);

            Merge(a, b);
            Merge(b, a);
            Assert.AreEqual(1250, a.Gems, "no free ship");
            Assert.AreEqual(1250, b.Gems);
            CollectionAssert.Contains(b.OwnedShips, "manta");
        }

        [Test]
        public void Gems_EarnedOnTwoDevices_AreAddedExactlyOnce()
        {
            PlayerProfile a = NewProfile("dev-a", 1000);
            PlayerProfile b = OnOtherDevice(a, "dev-b");
            Service(a).AddGems(100, "test");
            Service(b).AddGems(250, "test");

            Merge(a, b);
            Assert.AreEqual(1350, a.Gems);
            Merge(b, a);
            Assert.AreEqual(1350, b.Gems);

            // Syncing again (both directions, repeatedly) never duplicates anything.
            for (int i = 0; i < 3; i++)
            {
                Merge(a, b);
                Merge(b, a);
            }

            Assert.AreEqual(1350, a.Gems);
            Assert.AreEqual(1350, b.Gems);
        }

        [Test]
        public void Gems_BoughtOnADevice_SurviveAnOlderCloudCopy()
        {
            PlayerProfile a = NewProfile("dev-a", 100);
            PlayerProfile cloud = OnOtherDevice(a, "dev-cloud-writer");
            Service(a).GrantEntitlement("beamrunner.gems.small", false, 1000);
            Service(a).TrySpend(50);

            Merge(a, cloud);
            Assert.AreEqual(1050, a.Gems);
        }

        [Test]
        public void Gems_SpendingEverything_IsNotUndoneByAStaleSnapshot()
        {
            PlayerProfile a = NewProfile("dev-a", Price("manta"));
            PlayerProfile stale = Clone(a);
            Assert.AreEqual(ShipPurchaseResult.Purchased, Service(a).BuyShip("manta"));
            Assert.AreEqual(0, a.Gems);

            Merge(a, stale);
            Assert.AreEqual(0, a.Gems);
        }

        [Test]
        public void Gems_OfALegacySnapshotWithoutLedger_AreBookedOnce()
        {
            PlayerProfile a = NewProfile("dev-a", 0);
            var legacy = PlayerProfile.CreateNew();
            legacy.Gems = 500;
            legacy.Wallet = null;

            Merge(a, legacy);
            Assert.AreEqual(500, a.Gems);
            Merge(a, legacy);
            Assert.AreEqual(500, a.Gems, "merging the same legacy snapshot again adds nothing");
        }

        [Test]
        public void Wallet_MergeIsCommutativeAndIdempotent()
        {
            var x = new WalletLedger { Entries = { new WalletEntry { Device = "b", Earned = 5, Spent = 1 }, new WalletEntry { Device = "a", Earned = 3 } } };
            var y = new WalletLedger { Entries = { new WalletEntry { Device = "a", Earned = 4, Spent = 2 }, new WalletEntry { Device = "c", Earned = 1 } } };
            WalletLedger xy = CloneLedger(x);
            WalletLedger.MergeInto(xy, y);
            WalletLedger yx = CloneLedger(y);
            WalletLedger.MergeInto(yx, x);
            AssertDeepEqual(xy.Entries, yx.Entries);
            Assert.AreEqual(4 - 2 + 5 - 1 + 1, WalletLedger.Balance(xy));
            WalletLedger.MergeInto(xy, y);
            AssertDeepEqual(yx.Entries, xy.Entries);
        }

        [Test]
        public void Wallet_NormalizeRepairsGarbage()
        {
            var w = new WalletLedger
            {
                DeviceId = null,
                Entries = { null, new WalletEntry { Device = "", Earned = 9 }, new WalletEntry { Device = "z", Earned = -5, Spent = 3 },
                    new WalletEntry { Device = "z", Earned = 7, Spent = 1 } },
            };
            WalletLedger.Normalize(w);
            Assert.AreEqual(string.Empty, w.DeviceId);
            Assert.AreEqual(1, w.Entries.Count);
            Assert.AreEqual(7, w.Entries[0].Earned);
            Assert.AreEqual(3, w.Entries[0].Spent);
        }

        // ------------------------------------------------------------------ reset (epochs)

        [Test]
        public void ResetOnOneDevice_IsNotUndoneByTheOther_ButPurchasesStay()
        {
            PlayerProfile shared = NewProfile("dev-a", Price("manta") + 500);
            Assert.AreEqual(ShipPurchaseResult.Purchased, Service(shared).BuyShip("manta"));
            shared.Flags.AdsRemoved = true;
            PlayerProfile b = OnOtherDevice(shared, "dev-b");

            var sa = Service(shared);
            sa.ResetProgress();
            PlayerProfile a = sa.Profile;
            Assert.AreEqual(1, a.Sync.Epoch);
            Assert.AreEqual(0, a.Gems);

            ProfileMergeReport ra = Merge(a, b);
            Assert.IsTrue(ra.IgnoredOlderRemoteEpoch);
            CollectionAssert.DoesNotContain(a.OwnedShips, "manta");
            Assert.AreEqual(0, a.Gems, "the reset's gems stay gone (booked as spent)");

            ProfileMergeReport rb = Merge(b, a);
            Assert.IsTrue(rb.AdoptedRemoteEpoch);
            Assert.AreEqual(1, b.Sync.Epoch);
            CollectionAssert.DoesNotContain(b.OwnedShips, "manta");
            Assert.AreEqual(0, b.Gems);
            Assert.IsTrue(b.Flags.AdsRemoved);
            Assert.AreEqual("dev-b", b.Wallet.DeviceId, "device-local");
        }

        [Test]
        public void ResetOnOneDevice_KeepsGemsTheOtherDeviceEarnedMeanwhile()
        {
            PlayerProfile shared = NewProfile("dev-a", 1000);
            PlayerProfile b = OnOtherDevice(shared, "dev-b");
            var sa = Service(shared);
            sa.ResetProgress();
            Service(b).GrantEntitlement("beamrunner.gems.small", false, 1000);

            PlayerProfile a = sa.Profile;
            Merge(a, b);
            Assert.AreEqual(1000, a.Gems, "B's purchase is not lost to A's reset");
        }

        // ------------------------------------------------------------------ other rules

        [Test]
        public void DeviceLocalData_StaysLocal()
        {
            PlayerProfile a = NewProfile("dev-a");
            PlayerProfile b = OnOtherDevice(a, "dev-b");
            a.Settings.MusicVolume = 0.1f;
            a.Flags.ConsentAsked = true;
            a.Flags.PersonalizedAds = false;
            a.Sync.CloudId = "cloud-1";
            b.Settings.MusicVolume = 0.9f;
            b.Flags.PersonalizedAds = true;
            b.Sync.CloudId = "cloud-2";
            Service(b).ApplyRun(Run(100, 2000, 1)); // b dominant

            Merge(a, b);
            Assert.AreEqual(0.1f, a.Settings.MusicVolume);
            Assert.IsFalse(a.Flags.PersonalizedAds);
            Assert.AreEqual("cloud-1", a.Sync.CloudId);
            Assert.AreEqual("dev-a", a.Wallet.DeviceId);
        }

        [Test]
        public void SelectedShip_StaysLocalWhileOwned()
        {
            PlayerProfile a = NewProfile("dev-a", Price("viper"));
            PlayerProfile b = OnOtherDevice(a, "dev-b");
            MeetViperRequirement(b);
            Assert.AreEqual(ShipPurchaseResult.Purchased, Service(b).BuyShip("viper"));
            b.SelectedShip = "viper";
            Merge(a, b);
            Assert.AreEqual(ShipCatalog.DefaultShipId, a.SelectedShip);
        }

        [Test]
        public void LocalLeaderboard_IsTheUnionOfBothTopTens()
        {
            PlayerProfile a = NewProfile("dev-a");
            PlayerProfile b = OnOtherDevice(a, "dev-b");
            var sa = Service(a);
            var sb = Service(b);
            for (int i = 1; i <= 8; i++)
            {
                sa.ApplyRun(Run(i * 100, 500, 0));
                sb.ApplyRun(Run(i * 100 + 50, 500, 0));
            }

            Merge(a, b);
            Assert.AreEqual(ProfileService.LeaderboardSize, a.Leaderboard.Count);
            Assert.AreEqual(850, a.Leaderboard[0].Score);
            for (int i = 1; i < a.Leaderboard.Count; i++)
            {
                Assert.GreaterOrEqual(a.Leaderboard[i - 1].Score, a.Leaderboard[i].Score);
            }

            // Shared entries are not duplicated.
            PlayerProfile c = Clone(a);
            Merge(a, c);
            Assert.AreEqual(ProfileService.LeaderboardSize, a.Leaderboard.Count);
            Assert.AreNotEqual(a.Leaderboard[0].Score, a.Leaderboard[1].Score);
        }

        [Test]
        public void LoginCalendar_LaterClaimWins_SoAClaimedDayStaysClaimed()
        {
            PlayerProfile a = NewProfile("dev-a");
            PlayerProfile b = OnOtherDevice(a, "dev-b");
            a.Daily.LastClaimDate = "2026-09-19";
            a.Daily.Streak = 4;
            b.Daily.LastClaimDate = "2026-09-20";
            b.Daily.Streak = 1;

            Merge(a, b);
            Assert.AreEqual("2026-09-20", a.Daily.LastClaimDate);
            Assert.AreEqual(1, a.Daily.Streak);
            Assert.IsFalse(DailyRewards.CanClaim(a.Daily, Now), "today was claimed on the other device");
        }

        [Test]
        public void DailyRun_LaterDayWins_SameDayKeepsTheBetterBest()
        {
            PlayerProfile a = NewProfile("dev-a");
            PlayerProfile b = OnOtherDevice(a, "dev-b");
            int day = DailyRuns.DayIndex(new DateTime(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc));
            a.DailyRun.Day = day;
            a.DailyRun.Streak = 3;
            a.DailyRun.BestScore = 700;
            a.DailyRun.Attempts = 2;
            a.DailyRun.AllTimeBestScore = 5000;
            b.DailyRun.Day = day;
            b.DailyRun.Streak = 3;
            b.DailyRun.BestScore = 900;
            b.DailyRun.BestShip = "viper";
            b.DailyRun.Attempts = 1;
            b.DailyRun.TotalRuns = 40;

            Merge(a, b);
            Assert.AreEqual(900, a.DailyRun.BestScore);
            Assert.AreEqual("viper", a.DailyRun.BestShip);
            Assert.AreEqual(2, a.DailyRun.Attempts);
            Assert.AreEqual(5000, a.DailyRun.AllTimeBestScore);
            Assert.AreEqual(40, a.DailyRun.TotalRuns);

            PlayerProfile c = OnOtherDevice(a, "dev-c");
            c.DailyRun.Day = day + 1;
            c.DailyRun.BestScore = 10;
            c.DailyRun.Streak = 4;
            Merge(a, c);
            Assert.AreEqual(day + 1, a.DailyRun.Day);
            Assert.AreEqual(10, a.DailyRun.BestScore);
            Assert.AreEqual(4, a.DailyRun.Streak);
        }

        [Test]
        public void Missions_FromTheSnapshotWithMoreCompletedSets()
        {
            PlayerProfile a = NewProfile("dev-a");
            PlayerProfile b = OnOtherDevice(a, "dev-b");
            b.Missions.SetsCompleted = 2;
            b.PilotRank = 3;
            b.Missions.Active[0].Progress = 1;

            Merge(a, b);
            Assert.AreEqual(2, a.Missions.SetsCompleted);
            Assert.AreEqual(3, a.PilotRank);

            // Same set: progress is the maximum per mission.
            PlayerProfile c = OnOtherDevice(a, "dev-c");
            a.Missions.Active[1].Progress = 5;
            c.Missions.Active[1].Progress = 9;
            c.Missions.Active[2].Completed = true;
            Merge(a, c);
            Assert.AreEqual(9, a.Missions.Active[1].Progress);
            Assert.IsTrue(a.Missions.Active[2].Completed);
        }

        [Test]
        public void MergeOrder_DoesNotChangeTheProgress()
        {
            PlayerProfile a = NewProfile("dev-a", Price("manta") + Price("viper"));
            PlayerProfile b = OnOtherDevice(a, "dev-b");
            var sa = Service(a);
            var sb = Service(b);
            sa.ApplyRun(Run(6000, 2500, 30));
            Assert.AreEqual(ShipPurchaseResult.Purchased, sa.BuyShip("manta"));
            sb.ApplyRun(Run(ShipCatalog.Get("viper").UnlockValue, 1500, 90)); // meets the Viper's score requirement
            sb.ApplyRun(Run(2000, 800, 15));
            Assert.AreEqual(ShipPurchaseResult.Purchased, sb.BuyShip("viper"));

            PlayerProfile ab = Clone(a);
            Merge(ab, b);
            PlayerProfile ba = Clone(b);
            Merge(ba, a);

            Assert.AreEqual(ab.Gems, ba.Gems);
            CollectionAssert.AreEquivalent(ab.OwnedShips, ba.OwnedShips);
            CollectionAssert.AreEquivalent(ab.Achievements, ba.Achievements);
            AssertDeepEqual(ab.Stats, ba.Stats);
            Assert.AreEqual(ab.PilotRank, ba.PilotRank);
            AssertDeepEqual(ab.Leaderboard, ba.Leaderboard);
        }

        [Test]
        public void UnknownFields_ComeFromTheDominantSnapshot()
        {
            var target = new FutureData { Known = 1, Unknown = "local", Values = new List<int> { 1 } };
            var source = new FutureData { Known = 2, Unknown = "remote", Values = new List<int> { 2 } };
            ProfileMerge.CopyUnhandledFields(target, source, new HashSet<string> { "Known" });
            Assert.AreEqual(1, target.Known);
            Assert.AreEqual("remote", target.Unknown);
            CollectionAssert.AreEqual(new[] { 2 }, target.Values);

            PlayerProfile a = NewProfile("dev-a");
            PlayerProfile b = OnOtherDevice(a, "dev-b");
            Assert.IsFalse(ProfileMerge.RemoteIsDominant(a, b), "ties go to local");
            b.Stats.TotalPlaySeconds = 10;
            Assert.IsTrue(ProfileMerge.RemoteIsDominant(a, b));
        }

        [Serializable]
        private sealed class FutureData
        {
            public int Known;
            public string Unknown;
            public List<int> Values;
        }

        // ------------------------------------------------------------------ reflection helpers

        private static WalletLedger CloneLedger(WalletLedger w)
        {
            return (WalletLedger)DeepClone(w);
        }

        private static PlayerProfile Clone(PlayerProfile p)
        {
            return (PlayerProfile)DeepClone(p);
        }

        private static object DeepClone(object value)
        {
            if (value == null)
            {
                return null;
            }

            Type t = value.GetType();
            if (t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(decimal))
            {
                return value;
            }

            if (value is Array array)
            {
                var arrayCopy = Array.CreateInstance(t.GetElementType(), array.Length);
                for (int i = 0; i < array.Length; i++)
                {
                    arrayCopy.SetValue(DeepClone(array.GetValue(i)), i);
                }

                return arrayCopy;
            }

            if (value is IList list)
            {
                var copy = (IList)Activator.CreateInstance(t);
                foreach (object item in list)
                {
                    copy.Add(DeepClone(item));
                }

                return copy;
            }

            object clone = Activator.CreateInstance(t);
            foreach (FieldInfo f in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                f.SetValue(clone, DeepClone(f.GetValue(value)));
            }

            return clone;
        }

        private static void AssertDeepEqual(object expected, object actual, string path = "root")
        {
            if (expected == null || actual == null)
            {
                Assert.AreEqual(expected, actual, path);
                return;
            }

            Type t = expected.GetType();
            Assert.AreEqual(t, actual.GetType(), path);
            if (t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(decimal))
            {
                Assert.AreEqual(expected, actual, path);
                return;
            }

            if (expected is IList le)
            {
                var la = (IList)actual;
                Assert.AreEqual(le.Count, la.Count, path + ".Count");
                for (int i = 0; i < le.Count; i++)
                {
                    AssertDeepEqual(le[i], la[i], path + "[" + i + "]");
                }

                return;
            }

            foreach (FieldInfo f in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                AssertDeepEqual(f.GetValue(expected), f.GetValue(actual), path + "." + f.Name);
            }
        }
    }
}

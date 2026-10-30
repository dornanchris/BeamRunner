using System;
using BeamRunner.Core.Meta;
using BeamRunner.Core.Sim;
using NUnit.Framework;
using UnityEngine;

namespace BeamRunner.PlayModeTests
{
    /// <summary>
    /// The save format through Unity's real <see cref="JsonUtility"/> (what <c>ProfileStore</c> uses), without booting the
    /// game: the first released format (v1) is pinned as a literal so that the day a v2 arrives, "old saves still load" is
    /// already a test; missing fields fall back to their defaults; and a profile with a recorded Daily Run ghost survives a
    /// save/load round trip. The Core tests cover the same rules with plain objects; these pin the serializer's side
    /// (missing fields, nested objects, the Base64 ghost string).
    /// </summary>
    [Category("Save")]
    public sealed class SaveFormatTests
    {
        /// <summary>A v1 save as the first release writes it.</summary>
        private const string V1Save =
            "{\"Version\":1,\"Gems\":420,\"PilotRank\":3,\"SelectedShip\":\"manta\",\"OwnedShips\":[\"arrow\",\"shuttle\",\"manta\"]," +
            "\"Stats\":{\"BestScore\":9000,\"BestDistance\":1200.5,\"BestNearMissesRun\":4,\"BestNearMissChain\":3,\"BestMultiplier\":2," +
            "\"BestLevel\":6,\"BestGemsRun\":30,\"Runs\":12,\"TotalDistance\":8000.0,\"TotalGems\":300,\"TotalNearMisses\":40," +
            "\"TotalGapsBridged\":200,\"TotalCubesDodged\":950,\"TotalLevelsCleared\":17,\"TotalSmashes\":5,\"TotalOverloads\":6," +
            "\"TotalPowerUps\":7,\"TotalPlaySeconds\":600.0,\"Revives\":1}," +
            "\"Settings\":{\"MusicVolume\":0.5,\"SfxVolume\":0.7,\"Control\":2,\"Sensitivity\":1.2,\"InvertY\":true,\"StartLevel\":4," +
            "\"Haptics\":true,\"GraphicsQuality\":1,\"ReduceFlashing\":false,\"AutoOverdrive\":false,\"ShowFps\":false," +
            "\"CameraShake\":0.8,\"OverdriveButtonLeft\":false,\"HideGhost\":false}," +
            "\"Missions\":{\"Active\":[{\"Type\":11,\"Target\":3,\"Progress\":1,\"Reward\":200,\"Completed\":false}],\"SetsCompleted\":2}," +
            "\"Achievements\":[\"first_flight\",\"level_3\"],\"Daily\":{\"LastClaimDate\":\"2026-09-20\",\"Streak\":2}," +
            "\"Leaderboard\":[{\"Score\":9000,\"Distance\":1200.5,\"Date\":\"2026-09-20\",\"Ship\":\"manta\",\"Level\":6}]," +
            "\"Flags\":{\"TutorialDone\":true,\"ConsentAsked\":true,\"PersonalizedAds\":false,\"AdsRemoved\":false,\"RatePrompted\":false," +
            "\"GameOversSinceInterstitial\":1,\"LastInterstitialUtcTicks\":0,\"FirstLaunchUtcTicks\":638000000000000000," +
            "\"LastSeenVersion\":\"1.0.0\"}}";

        [Test]
        public void V1Save_LoadsWithEverythingKept()
        {
            PlayerProfile p = JsonUtility.FromJson<PlayerProfile>(V1Save);
            Assert.IsNotNull(p);
            p.Sanitize();

            Assert.AreEqual(1, p.Version);
            Assert.AreEqual(PlayerProfile.CurrentVersion, p.Version);
            Assert.AreEqual(420, p.Gems);
            Assert.AreEqual(3, p.PilotRank);
            Assert.AreEqual("manta", p.SelectedShip);
            Assert.AreEqual(9000, p.Stats.BestScore);
            Assert.AreEqual(6, p.Stats.BestLevel);
            Assert.AreEqual(950, p.Stats.TotalCubesDodged);
            Assert.AreEqual(17, p.Stats.TotalLevelsCleared);
            Assert.AreEqual(12, p.Stats.Runs);
            Assert.AreEqual(0.5f, p.Settings.MusicVolume);
            Assert.AreEqual(ControlMode.Swipe, p.Settings.ControlMode, "an old steering mode falls back to swipe");
            Assert.IsTrue(p.Settings.InvertY);
            Assert.AreEqual(4, p.Settings.StartLevel);
            Assert.AreEqual(MissionType.LevelsClearedRun, p.Missions.Active[0].MissionType);
            Assert.AreEqual(2, p.Missions.SetsCompleted);
            Assert.AreEqual(6, p.Leaderboard[0].Level);
            Assert.AreEqual("2026-09-20", p.Daily.LastClaimDate);
            Assert.IsTrue(p.Flags.TutorialDone);
            Assert.IsNotNull(p.DailyRun, "no DailyRun object in the file: a fresh one");
            Assert.IsTrue(p.Settings.ShowGhost);

            var service = new ProfileService(p, null, 1);
            Assert.AreEqual(6, service.MaxStartLevel);
            Assert.AreEqual(4, service.StartLevel);
            Assert.AreEqual(4, service.CreateRunConfig(1).StartLevel);
            Assert.IsTrue(service.DailyRunUnlocked);
            Assert.AreEqual(0, service.DailyRunStreak);
            Assert.IsNull(service.BeginDailyRun().Ghost);
        }

        [Test]
        public void MissingFields_FallBackToDefaults()
        {
            PlayerProfile p = JsonUtility.FromJson<PlayerProfile>("{\"Version\":1,\"Gems\":5,\"Settings\":{\"MusicVolume\":0.3}}");
            p.Sanitize();
            Assert.AreEqual(1, p.Settings.StartLevel, "no StartLevel: level 1");
            Assert.IsFalse(p.Settings.InvertY);
            Assert.AreEqual(ControlMode.Swipe, p.Settings.ControlMode);
            Assert.AreEqual(0, p.Stats.BestLevel);
            Assert.IsNotNull(p.DailyRun);
            Assert.IsTrue(p.Settings.ShowGhost);
            Assert.AreEqual(1, new ProfileService(p, null, 1).MaxStartLevel);

            // A start level above the levels reached is clamped when a run starts (a hand-edited or synced setting).
            p.Settings.StartLevel = 9;
            p.Flags.TutorialDone = true;
            Assert.AreEqual(1, new ProfileService(p, null, 1).CreateRunConfig(1).StartLevel);
        }

        [Test]
        public void DailyRunWithGhost_RoundTripsThroughJsonUtility()
        {
            DateTime utc = new DateTime(2026, 9, 26, 12, 0, 0, DateTimeKind.Utc);
            int day = DailyRuns.DayIndex(utc);
            var recorder = new GhostRecorder(DailyRuns.SeedForDay(day), DailyRuns.GeneratorVersion);
            for (int i = 0; i <= 3000; i++)
            {
                double d = i * 1.1;
                recorder.Record(d, (float)Math.Sin(d / 25.0) * 6f, (float)Math.Cos(d / 31.0) * 4f, d / 50.0);
            }

            GhostPath ghost = recorder.ToPath();
            PlayerProfile p = PlayerProfile.CreateNew();
            p.Flags.TutorialDone = true;
            p.Settings.ShowGhost = false;
            p.DailyRun.Day = day;
            p.DailyRun.Streak = 4;
            p.DailyRun.BestStreak = 6;
            p.DailyRun.BestScore = 12345;
            p.DailyRun.BestDistance = 3300f;
            p.DailyRun.BestShip = "manta";
            p.DailyRun.Attempts = 3;
            p.DailyRun.Ghost = ghost.Encode();

            string json = JsonUtility.ToJson(p);
            StringAssert.Contains("\"DailyRun\"", json);
            StringAssert.Contains("\"HideGhost\":true", json);

            PlayerProfile back = JsonUtility.FromJson<PlayerProfile>(json);
            back.Sanitize();
            Assert.AreEqual(day, back.DailyRun.Day);
            Assert.AreEqual(4, back.DailyRun.Streak);
            Assert.AreEqual(6, back.DailyRun.BestStreak);
            Assert.AreEqual(12345, back.DailyRun.BestScore);
            Assert.AreEqual(3300f, back.DailyRun.BestDistance);
            Assert.AreEqual("manta", back.DailyRun.BestShip);
            Assert.AreEqual(3, back.DailyRun.Attempts);
            Assert.AreEqual(ghost.Encode(), back.DailyRun.Ghost);
            Assert.IsFalse(back.Settings.ShowGhost);

            var service = new ProfileService(back, null, 1) { UtcClock = () => utc };
            GhostPath loaded = service.BeginDailyRun().Ghost;
            Assert.IsNotNull(loaded, "the stored ghost must come back for its own day");
            Assert.AreEqual(ghost.SampleCount, loaded.SampleCount);
            float ax;
            float ay;
            float bx;
            float by;
            ghost.PositionAt(1234.5, out ax, out ay);
            loaded.PositionAt(1234.5, out bx, out by);
            Assert.AreEqual(ax, bx);
            Assert.AreEqual(ay, by);
        }
    }
}

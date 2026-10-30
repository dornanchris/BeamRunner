using System;
using System.Collections.Generic;
using BeamRunner.Core.Balance;
using BeamRunner.Core.Field;

namespace BeamRunner.Core.Meta
{
    /// <summary>Lane-switching scheme (keyboard and gamepad always work too).</summary>
    public enum ControlMode
    {
        /// <summary>Swipe left or right anywhere to change lanes, up or down to build a ramp; a tap builds a bridge (default).</summary>
        Swipe = 0,

        /// <summary>Tap the outer thirds of the screen to change lanes, the middle to bridge; swipes still build ramps.</summary>
        TapSides = 1,
    }

    /// <summary>
    /// Everything persisted for the player. Plain serialisable fields only (Unity's JsonUtility compatible:
    /// public fields, <see cref="List{T}"/>, nested [Serializable] classes; no dictionaries or properties).
    /// Always call <see cref="Sanitize"/> after loading.
    /// </summary>
    [Serializable]
    public sealed class PlayerProfile
    {
        /// <summary>
        /// Save format version. 1: initial release. Raise it when a change needs a migration step in <see cref="Sanitize"/>
        /// (and add a save-format test that loads the previous version).
        /// </summary>
        public const int CurrentVersion = 1;

        public int Version = CurrentVersion;
        public long Gems;
        public int PilotRank = 1;
        public string SelectedShip = ShipCatalog.DefaultShipId;
        public List<string> OwnedShips = new List<string>();
        public PlayerStats Stats = new PlayerStats();
        public PlayerSettings Settings = new PlayerSettings();
        public MissionState Missions = new MissionState();
        public List<string> Achievements = new List<string>();
        public DailyRewardState Daily = new DailyRewardState();
        public List<LeaderboardEntry> Leaderboard = new List<LeaderboardEntry>();
        public ProfileFlags Flags = new ProfileFlags();

        /// <summary>Daily Run best/streak/ghost (see <see cref="DailyRuns"/>).</summary>
        public DailyRunState DailyRun = new DailyRunState();

        /// <summary>
        /// Per-device gem counters that explain <see cref="Gems"/> (see <see cref="WalletLedger"/>):
        /// lets cloud saves merge the wallet without duplicating or losing gems.
        /// </summary>
        public WalletLedger Wallet = new WalletLedger();

        /// <summary>Cloud-save bookkeeping (see <see cref="ProfileMerge"/>).</summary>
        public CloudSyncState Sync = new CloudSyncState();

        /// <summary>Craft upgrade levels, indexed by <see cref="UpgradeId"/> (merged per stat by the higher level).</summary>
        public int[] Upgrades = new int[ShipStats.UpgradeCount];

        /// <summary>The hidden adaptive-difficulty rating and its history (never shown as a number).</summary>
        public SkillState Skill = new SkillState();

        /// <summary>UTC day (yyyy-MM-dd) of the last free upgrade token claimed through a rewarded ad.</summary>
        public string FreeUpgradeDay = string.Empty;

        /// <summary>Free upgrade tokens claimed on <see cref="FreeUpgradeDay"/>.</summary>
        public int FreeUpgradesToday;

        /// <summary>A fresh profile with the starter ships.</summary>
        public static PlayerProfile CreateNew()
        {
            var p = new PlayerProfile();
            p.Sanitize();
            return p;
        }

        /// <summary>
        /// Repairs anything a corrupt, tampered or older save could contain: nulls, out-of-range values,
        /// unknown ship ids, missing starter ships. Idempotent.
        /// </summary>
        public void Sanitize()
        {
            if (Version < 1)
            {
                Version = 1;
            }

            if (Version > CurrentVersion)
            {
                // Written by a newer build: keep data, but don't pretend we understand newer fields.
                Version = CurrentVersion;
            }

            if (Gems < 0)
            {
                Gems = 0;
            }

            PilotRank = MathUtil.Clamp(PilotRank, 1, 999);
            Stats = Stats ?? new PlayerStats();
            Stats.BestLevel = MathUtil.Clamp(Stats.BestLevel, 0, Levels.MaxLevel);
            Settings = Settings ?? new PlayerSettings();
            Settings.Sanitize();
            Missions = Missions ?? new MissionState();
            Missions.Active = Missions.Active ?? new List<Mission>();
            Achievements = Achievements ?? new List<string>();
            Daily = Daily ?? new DailyRewardState();
            Leaderboard = Leaderboard ?? new List<LeaderboardEntry>();
            Flags = Flags ?? new ProfileFlags();
            DailyRun = DailyRun ?? new DailyRunState();
            DailyRun.Sanitize();
            Wallet = Wallet ?? new WalletLedger();
            WalletLedger.Normalize(Wallet);
            Sync = Sync ?? new CloudSyncState();
            Sync.CloudId = Sync.CloudId ?? string.Empty;
            Sync.Epoch = Math.Max(0, Sync.Epoch);
            SanitizeUpgrades();
            Skill = Skill ?? new SkillState();
            Skill.Sanitize();
            FreeUpgradeDay = FreeUpgradeDay ?? string.Empty;
            FreeUpgradesToday = Math.Max(0, FreeUpgradesToday);
            OwnedShips = OwnedShips ?? new List<string>();

            // Drop unknown / duplicate ships; always own the free ones.
            var seen = new HashSet<string>();
            for (int i = OwnedShips.Count - 1; i >= 0; i--)
            {
                string id = OwnedShips[i];
                if (string.IsNullOrEmpty(id) || !ShipCatalog.Exists(id) || !seen.Add(id))
                {
                    OwnedShips.RemoveAt(i);
                }
            }

            IReadOnlyList<ShipDefinition> all = ShipCatalog.All;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i].Unlock == UnlockKind.Free && !OwnedShips.Contains(all[i].Id))
                {
                    OwnedShips.Add(all[i].Id);
                }
            }

            if (string.IsNullOrEmpty(SelectedShip) || !OwnedShips.Contains(SelectedShip))
            {
                SelectedShip = ShipCatalog.DefaultShipId;
            }

            for (int i = Leaderboard.Count - 1; i >= 0; i--)
            {
                if (Leaderboard[i] == null)
                {
                    Leaderboard.RemoveAt(i);
                }
            }

            for (int i = Missions.Active.Count - 1; i >= 0; i--)
            {
                if (Missions.Active[i] == null)
                {
                    Missions.Active.RemoveAt(i);
                }
            }
        }

        /// <summary>Upgrade levels: always one per stat, each within 0 … its maximum.</summary>
        private void SanitizeUpgrades()
        {
            if (Upgrades == null || Upgrades.Length != ShipStats.UpgradeCount)
            {
                var fixedLevels = new int[ShipStats.UpgradeCount];
                if (Upgrades != null)
                {
                    Array.Copy(Upgrades, fixedLevels, Math.Min(Upgrades.Length, fixedLevels.Length));
                }

                Upgrades = fixedLevels;
            }

            BalanceConfig b = BalanceConfig.Current;
            for (int i = 0; i < Upgrades.Length; i++)
            {
                Upgrades[i] = MathUtil.Clamp(Upgrades[i], 0, b.MaxLevel((UpgradeId)i));
            }
        }

    }

    /// <summary>Lifetime statistics and personal bests.</summary>
    [Serializable]
    public sealed class PlayerStats
    {
        public long BestScore;
        public float BestDistance;
        public int BestNearMissesRun;
        public int BestNearMissChain;
        public int BestMultiplier = 1;

        /// <summary>Highest level reached in any run (1-based; 0 before the first run). Runs can start at any level up to it.</summary>
        public int BestLevel;

        public int BestGemsRun;
        public int Runs;
        public double TotalDistance;
        public long TotalGems;
        public long TotalNearMisses;
        public long TotalGapsBridged;
        public long TotalCubesDodged;
        public long TotalLevelsCleared;
        public long TotalSmashes;
        public long TotalOverloads;
        public long TotalPowerUps;

        /// <summary>Pieces built in all runs, and the most in one run.</summary>
        public long TotalBuilds;

        public int BestBuildsRun;

        /// <summary>Upgrade levels bought (all stats).</summary>
        public int UpgradesBought;
        public double TotalPlaySeconds;
        public int Revives;
    }

    /// <summary>User preferences.</summary>
    [Serializable]
    public sealed class PlayerSettings
    {
        public float MusicVolume = 0.8f;
        public float SfxVolume = 0.9f;
        public int Control = (int)ControlMode.Swipe;
        public float Sensitivity = 1f;
        /// <summary>Flight-style vertical control: moving the finger (stick, device) down flies the craft up.</summary>
        public bool InvertY;

        /// <summary>Level the next normal run starts at (clamped to the levels reached; see <see cref="ProfileService.StartLevel"/>).</summary>
        public int StartLevel = 1;
        public bool Haptics = true;

        /// <summary>0 = low, 1 = medium, 2 = high, −1 = auto-detect on first launch.</summary>
        public int GraphicsQuality = -1;

        public bool ReduceFlashing;
        public bool AutoOverdrive;
        public bool ShowFps;
        public float CameraShake = 1f;
        public bool OverdriveButtonLeft;

        /// <summary>
        /// Daily Run ghost turned off. Stored inverted so that a missing field reads as "ghost on", the default, whatever
        /// the deserialiser does with field initialisers. Use <see cref="ShowGhost"/>.
        /// </summary>
        public bool HideGhost;

        public ControlMode ControlMode
        {
            get { return (ControlMode)Control; }
            set { Control = (int)value; }
        }

        /// <summary>Show the ghost of today's best run during Daily Run attempts (Settings, default on).</summary>
        public bool ShowGhost
        {
            get { return !HideGhost; }
            set { HideGhost = !value; }
        }

        public void Sanitize()
        {
            MusicVolume = MathUtil.Clamp01(MusicVolume);
            SfxVolume = MathUtil.Clamp01(SfxVolume);
            Sensitivity = MathUtil.Clamp(Sensitivity, 0.4f, 2.5f);
            CameraShake = MathUtil.Clamp01(CameraShake);
            GraphicsQuality = MathUtil.Clamp(GraphicsQuality, -1, 2);
            StartLevel = MathUtil.Clamp(StartLevel, 1, Levels.MaxLevel);
            if (Control < 0 || Control > 1)
            {
                Control = (int)ControlMode.Swipe;
            }
        }
    }

    /// <summary>One-off flags and bookkeeping.</summary>
    [Serializable]
    public sealed class ProfileFlags
    {
        public bool TutorialDone;
        public bool ConsentAsked;
        public bool PersonalizedAds;
        public bool AdsRemoved;
        public bool RatePrompted;
        public int GameOversSinceInterstitial;
        public long LastInterstitialUtcTicks;
        public long FirstLaunchUtcTicks;
        public string LastSeenVersion = string.Empty;
    }

    /// <summary>One row of the local high-score table.</summary>
    [Serializable]
    public sealed class LeaderboardEntry
    {
        public long Score;
        public float Distance;
        public string Date = string.Empty;
        public string Ship = string.Empty;

        /// <summary>Highest level the run reached.</summary>
        public int Level;
    }
}

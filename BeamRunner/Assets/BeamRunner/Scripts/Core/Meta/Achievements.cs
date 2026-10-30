using System;
using System.Collections.Generic;
using BeamRunner.Core.Localization;

namespace BeamRunner.Core.Meta
{
    /// <summary>A permanent accomplishment with a one-time gem reward.</summary>
    public sealed class AchievementDefinition
    {
        public readonly string Id;

        /// <summary>String-table key of the title (upper-case in English).</summary>
        public readonly string TitleKey;

        /// <summary>String-table key of the description; it may use <c>{n}</c> (see <see cref="DescriptionCount"/>).</summary>
        public readonly string DescriptionKey;

        /// <summary>The number the description shows as <c>{n}</c> (also its plural count); 0 when it shows none.</summary>
        public readonly long DescriptionCount;

        public readonly int Reward;

        private readonly Func<PlayerProfile, double> _value;
        private readonly double _target;

        public AchievementDefinition(string id, string titleKey, string descriptionKey, int reward, double target,
            Func<PlayerProfile, double> value, long descriptionCount = 0)
        {
            Id = id;
            TitleKey = titleKey;
            DescriptionKey = descriptionKey;
            DescriptionCount = descriptionCount;
            Reward = reward;
            _target = target;
            _value = value;
        }

        /// <summary>Title in the current language.</summary>
        public string Title
        {
            get { return Loc.T(TitleKey); }
        }

        /// <summary>Description in the current language, e.g. "Fly 1,000 m in one run".</summary>
        public string Description
        {
            get { return Loc.T(DescriptionKey, ("n", DescriptionCount)); }
        }

        public double Target
        {
            get { return _target; }
        }

        public double Value(PlayerProfile profile)
        {
            return _value(profile);
        }

        public bool IsMet(PlayerProfile profile)
        {
            return _value(profile) >= _target;
        }

        public float Progress01(PlayerProfile profile)
        {
            return _target <= 0 ? 1f : MathUtil.Clamp01((float)(_value(profile) / _target));
        }
    }

    /// <summary>All achievements. Evaluated against lifetime stats after each run and purchase.</summary>
    public static class AchievementCatalog
    {
        private static readonly AchievementDefinition[] Defs =
        {
            new AchievementDefinition("first_flight", "ach.first_flight", "ach.first_flight_desc", 50, 1, p => p.Stats.Runs),
            new AchievementDefinition("km_1", "ach.km_1", "ach.desc_fly_run", 100, 1000, p => p.Stats.BestDistance, 1000),
            new AchievementDefinition("km_5", "ach.km_5", "ach.desc_fly_run", 300, 5000, p => p.Stats.BestDistance, 5000),
            new AchievementDefinition("km_10", "ach.km_10", "ach.desc_fly_run", 1000, 10000, p => p.Stats.BestDistance, 10000),
            new AchievementDefinition("level_3", "ach.level_3", "ach.desc_reach_level", 100, 3, p => p.Stats.BestLevel, 3),
            new AchievementDefinition("level_6", "ach.level_6", "ach.desc_reach_level", 300, 6, p => p.Stats.BestLevel, 6),
            new AchievementDefinition("level_11", "ach.level_11", "ach.desc_reach_level", 800, 11, p => p.Stats.BestLevel, 11),
            new AchievementDefinition("level_15", "ach.level_15", "ach.desc_reach_level", 1500, 15, p => p.Stats.BestLevel, 15),
            new AchievementDefinition("close_10", "ach.close_10", "ach.desc_near_misses_run", 100, 10, p => p.Stats.BestNearMissesRun, 10),
            new AchievementDefinition("close_30", "ach.close_30", "ach.desc_near_misses_run", 400, 30, p => p.Stats.BestNearMissesRun, 30),
            new AchievementDefinition("chain_25", "ach.chain_25", "ach.chain_25_desc", 300, 25, p => p.Stats.BestNearMissChain, 25),
            new AchievementDefinition("mult_8", "ach.mult_8", "ach.mult_8_desc", 500, 8, p => p.Stats.BestMultiplier, 8),
            new AchievementDefinition("bridges_100", "ach.bridges_100", "ach.desc_bridges", 150, 100, p => p.Stats.TotalGapsBridged, 100),
            new AchievementDefinition("bridges_1000", "ach.bridges_1000", "ach.desc_bridges", 600, 1000, p => p.Stats.TotalGapsBridged, 1000),
            new AchievementDefinition("dodge_100", "ach.dodge_100", "ach.desc_dodge", 150, 100, p => p.Stats.TotalCubesDodged, 100),
            new AchievementDefinition("dodge_1000", "ach.dodge_1000", "ach.desc_dodge", 600, 1000, p => p.Stats.TotalCubesDodged, 1000),
            new AchievementDefinition("od_1", "ach.od_1", "ach.od_1_desc", 50, 1, p => p.Stats.TotalOverloads),
            new AchievementDefinition("od_50", "ach.od_50", "ach.od_50_desc", 400, 50, p => p.Stats.TotalOverloads, 50),
            new AchievementDefinition("builds_500", "ach.builds_500", "ach.desc_builds", 150, 500, p => p.Stats.TotalBuilds, 500),
            new AchievementDefinition("builds_5000", "ach.builds_5000", "ach.desc_builds", 600, 5000, p => p.Stats.TotalBuilds, 5000),
            new AchievementDefinition("upgrades_10", "ach.upgrades_10", "ach.upgrades_10_desc", 200, 10, p => p.Stats.UpgradesBought, 10),
            new AchievementDefinition("smash_25", "ach.smash_25", "ach.desc_smash", 150, 25, p => p.Stats.TotalSmashes, 25),
            new AchievementDefinition("smash_250", "ach.smash_250", "ach.desc_smash", 600, 250, p => p.Stats.TotalSmashes, 250),
            new AchievementDefinition("gems_1k", "ach.gems_1k", "ach.desc_collect_gems", 150, 1000, p => p.Stats.TotalGems, 1000),
            new AchievementDefinition("gems_10k", "ach.gems_10k", "ach.desc_collect_gems", 800, 10000, p => p.Stats.TotalGems, 10000),
            new AchievementDefinition("score_50k", "ach.score_50k", "ach.desc_score_run", 300, 50000, p => p.Stats.BestScore, 50000),
            new AchievementDefinition("score_250k", "ach.score_250k", "ach.desc_score_run", 1500, 250000, p => p.Stats.BestScore, 250000),
            new AchievementDefinition("fleet_4", "ach.fleet_4", "ach.fleet_4_desc", 300, 4, p => p.OwnedShips.Count, 4),
            new AchievementDefinition("fleet_all", "ach.fleet_all", "ach.fleet_all_desc", 1000, ShipCatalog.All.Count, p => p.OwnedShips.Count),
            new AchievementDefinition("comeback", "ach.comeback", "ach.comeback_desc", 50, 1, p => p.Stats.Revives),
            new AchievementDefinition("runs_50", "ach.runs_50", "ach.runs_50_desc", 300, 50, p => p.Stats.Runs, 50),
            new AchievementDefinition("rank_10", "ach.rank_10", "ach.rank_10_desc", 600, 10, p => p.PilotRank, 10),
        };

        public static IReadOnlyList<AchievementDefinition> All
        {
            get { return Defs; }
        }

        public static AchievementDefinition Get(string id)
        {
            for (int i = 0; i < Defs.Length; i++)
            {
                if (Defs[i].Id == id)
                {
                    return Defs[i];
                }
            }

            return null;
        }

        /// <summary>
        /// Unlocks every newly met achievement (adding ids to the profile) and returns them. Rewards are NOT
        /// granted here — <see cref="ProfileService"/> does that so all currency changes go through one place.
        /// </summary>
        public static List<AchievementDefinition> Evaluate(PlayerProfile profile)
        {
            var unlocked = new List<AchievementDefinition>();
            for (int i = 0; i < Defs.Length; i++)
            {
                AchievementDefinition def = Defs[i];
                if (!profile.Achievements.Contains(def.Id) && def.IsMet(profile))
                {
                    profile.Achievements.Add(def.Id);
                    unlocked.Add(def);
                }
            }

            return unlocked;
        }
    }
}

using System;
using BeamRunner.Core.Balance;
using BeamRunner.Core.Sim;

namespace BeamRunner.Core.Meta
{
    /// <summary>Outcome of trying to buy an upgrade level.</summary>
    public enum UpgradePurchaseResult
    {
        Purchased = 0,
        NotEnoughGems,

        /// <summary>The pilot has not reached the upgrade's unlock level yet.</summary>
        Locked,

        /// <summary>Already at its highest level.</summary>
        Maxed,
    }

    /// <summary>
    /// Upgrade shop and adaptive-difficulty part of the profile rules: levels are bought with gems at
    /// <see cref="BalanceConfig.UpgradeCost"/> (C0 × growth^level), gated by the best level reached, and once a day a
    /// rewarded ad grants a free level (<see cref="FreeUpgradesPerDay"/>). The craft of the next run follows the levels
    /// (<see cref="Ship"/>); the hidden skill rating moves after every counted run (<see cref="ApplySkill"/>).
    /// </summary>
    public sealed partial class ProfileService
    {
        /// <summary>Free upgrade levels a rewarded ad may grant per UTC day.</summary>
        public const int FreeUpgradesPerDay = 1;

        /// <summary>Raised after an upgrade level was bought or granted (id, new level).</summary>
        public event Action<UpgradeId, int> UpgradeChanged;

        /// <summary>The craft the next run uses.</summary>
        public ShipStats Ship
        {
            get { return ShipStats.FromLevels(Profile.Upgrades); }
        }

        /// <summary>Modifiers the next run is played at (from the hidden rating).</summary>
        public DifficultyModifiers NextRunModifiers
        {
            get { return AdaptiveDifficulty.For(Profile.Skill.Rating); }
        }

        public int UpgradeLevel(UpgradeId id)
        {
            int i = (int)id;
            return i >= 0 && i < Profile.Upgrades.Length ? Profile.Upgrades[i] : 0;
        }

        public int UpgradeMaxLevel(UpgradeId id)
        {
            return BalanceConfig.Current.MaxLevel(id);
        }

        public bool UpgradeMaxed(UpgradeId id)
        {
            return UpgradeLevel(id) >= UpgradeMaxLevel(id);
        }

        /// <summary>True once the pilot has reached the level that unlocks <paramref name="id"/>.</summary>
        public bool UpgradeUnlocked(UpgradeId id)
        {
            return AllStartLevelsUnlocked || Profile.Stats.BestLevel >= BalanceConfig.Current.UnlockLevel(id);
        }

        /// <summary>Level the pilot must reach to unlock <paramref name="id"/>.</summary>
        public int UpgradeUnlockLevel(UpgradeId id)
        {
            return BalanceConfig.Current.UnlockLevel(id);
        }

        /// <summary>Price of the next level (0 when maxed).</summary>
        public long UpgradeCost(UpgradeId id)
        {
            return UpgradeMaxed(id) ? 0 : BalanceConfig.Current.UpgradeCost(id, UpgradeLevel(id));
        }

        public bool CanBuyUpgrade(UpgradeId id)
        {
            return UpgradeUnlocked(id) && !UpgradeMaxed(id) && Profile.Gems >= UpgradeCost(id);
        }

        public UpgradePurchaseResult BuyUpgrade(UpgradeId id)
        {
            if (UpgradeMaxed(id))
            {
                return UpgradePurchaseResult.Maxed;
            }

            if (!UpgradeUnlocked(id))
            {
                return UpgradePurchaseResult.Locked;
            }

            if (!TrySpend(UpgradeCost(id)))
            {
                return UpgradePurchaseResult.NotEnoughGems;
            }

            RaiseUpgrade(id);
            return UpgradePurchaseResult.Purchased;
        }

        /// <summary>True if a rewarded ad may grant a free upgrade level today (UTC).</summary>
        public bool FreeUpgradeAvailable(DateTime utcNow)
        {
            return FreeUpgradesUsed(utcNow) < FreeUpgradesPerDay;
        }

        /// <summary>
        /// Grants one free level of <paramref name="id"/> (the daily rewarded-ad token). False when today's tokens are used,
        /// the upgrade is locked or maxed.
        /// </summary>
        public bool GrantFreeUpgrade(UpgradeId id, DateTime utcNow)
        {
            if (!FreeUpgradeAvailable(utcNow) || !UpgradeUnlocked(id) || UpgradeMaxed(id))
            {
                return false;
            }

            string day = utcNow.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            if (Profile.FreeUpgradeDay != day)
            {
                Profile.FreeUpgradeDay = day;
                Profile.FreeUpgradesToday = 0;
            }

            Profile.FreeUpgradesToday++;
            RaiseUpgrade(id);
            return true;
        }

        /// <summary>
        /// The cheapest upgrade the pilot can work toward now (unlocked, not maxed), or null when every one is maxed or
        /// locked. Drives the results screen's "next upgrade" bar.
        /// </summary>
        public UpgradeId? CheapestUpgrade()
        {
            UpgradeId? best = null;
            long bestCost = long.MaxValue;
            for (int i = 0; i < ShipStats.UpgradeCount; i++)
            {
                var id = (UpgradeId)i;
                if (!UpgradeUnlocked(id) || UpgradeMaxed(id))
                {
                    continue;
                }

                long cost = UpgradeCost(id);
                if (cost < bestCost)
                {
                    bestCost = cost;
                    best = id;
                }
            }

            return best;
        }

        /// <summary>0..1 progress of the wallet toward <see cref="CheapestUpgrade"/> (1 = affordable now).</summary>
        public float NextUpgradeProgress01()
        {
            UpgradeId? next = CheapestUpgrade();
            if (next == null)
            {
                return 1f;
            }

            long cost = UpgradeCost(next.Value);
            return cost <= 0 ? 1f : MathUtil.Clamp01((float)((double)Profile.Gems / cost));
        }

        private int FreeUpgradesUsed(DateTime utcNow)
        {
            string day = utcNow.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            return Profile.FreeUpgradeDay == day ? Profile.FreeUpgradesToday : 0;
        }

        private void RaiseUpgrade(UpgradeId id)
        {
            int i = (int)id;
            Profile.Upgrades[i] = Math.Min(UpgradeMaxLevel(id), Profile.Upgrades[i] + 1);
            Profile.Stats.UpgradesBought++;
            UpgradeChanged?.Invoke(id, Profile.Upgrades[i]);
            NotifyChanged();
        }

        /// <summary>
        /// Moves the hidden rating after a counted run (not the tutorial or a Daily Run, whose rules are fixed).
        /// Returns the change.
        /// </summary>
        private float ApplySkill(RunResult r, bool counted)
        {
            if (!counted || r == null || !r.Adaptive)
            {
                return 0f;
            }

            float performance = AdaptiveDifficulty.Performance(r, Ship);
            return AdaptiveDifficulty.ApplyRun(Profile.Skill, performance, r.Duration);
        }
    }
}

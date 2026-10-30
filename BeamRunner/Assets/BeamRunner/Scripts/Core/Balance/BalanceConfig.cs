using System;

namespace BeamRunner.Core.Balance
{
    /// <summary>
    /// Every tunable number of building, fuel, the hull, progression and adaptive difficulty in one place
    /// (docs/ARCHITECTURE.md §5, "Balance"). The defaults below are the shipped tuning (the design doc's starting values);
    /// <c>Resources/BeamRunner/Config/balance.json</c> may override any field without a code change (the Unity layer loads
    /// it into <see cref="Current"/> at boot; a missing or broken file keeps the defaults). Plain public fields so
    /// <c>JsonUtility</c> can read it.
    /// </summary>
    [Serializable]
    public sealed class BalanceConfig
    {
        // ------------------------------------------------------------------ building and fuel

        /// <summary>Fuel a bridge (tap) costs.</summary>
        public float BridgeCost = 2f;

        /// <summary>Fuel a ramp up (swipe up) costs.</summary>
        public float RampUpCost = 3f;

        /// <summary>Fuel a ramp down (swipe down) costs.</summary>
        public float RampDownCost = 1f;

        /// <summary>Fuel at the start of a run (before upgrades).</summary>
        public float StartFuel = 10f;

        /// <summary>Most fuel the craft holds (before upgrades).</summary>
        public float MaxFuel = 20f;

        /// <summary>Fuel one fuel cell gives.</summary>
        public float FuelPickupValue = 3f;

        /// <summary>Chance that a tile of plain track between chunks carries a fuel cell (before the scales).</summary>
        public float FillerFuelChance = 0.12f;

        /// <summary>
        /// Seconds after the craft enters a gap during which a build still fills that tile (a forgiving last-second build)
        /// instead of the next one; the craft only starts to fall once it is over.
        /// </summary>
        public float LateBuildSeconds = 0.08f;

        // ------------------------------------------------------------------ hull

        /// <summary>Hull points at the start of a run (before upgrades). A one-layer fall costs one.</summary>
        public int HullBase = 3;

        /// <summary>Most hull points upgrades can give.</summary>
        public int HullCap = 5;

        // ------------------------------------------------------------------ gems

        /// <summary>Gems one pickup is worth at level 1.</summary>
        public float GemValueBase = 1f;

        /// <summary>Extra gems per pickup for every level past the first (gem income scales with level).</summary>
        public float GemValuePerLevel = 0.15f;

        /// <summary>Gems per gem cell (two per tile reads as a trail).</summary>
        public int GemsPerCell = 2;

        // ------------------------------------------------------------------ power-ups

        public float OverloadSeconds = 4f;
        public float OverloadSpeedMultiplier = 1.35f;

        /// <summary>Seconds the craft keeps hovering when Overload ends over a gap (the design's grace period).</summary>
        public float OverloadGrace = 0.8f;

        public float MagnetSeconds = 8f;
        public float InstantBuildSeconds = 5f;

        /// <summary>Seconds of flight between power-ups (at the Standard rating; see <see cref="RatingDrops"/>).</summary>
        public float PowerUpIntervalMin = 12f;

        public float PowerUpIntervalMax = 20f;

        /// <summary>Relative chance of each power-up: shield, magnet, Overload, instant build, repair.</summary>
        public float[] PowerUpWeights = { 26f, 20f, 18f, 22f, 8f };

        // ------------------------------------------------------------------ upgrades (indexed by UpgradeId)

        /// <summary>Highest level of each upgrade: fuel tank, build efficiency, starting fuel, power-up duration, hull.</summary>
        public int[] MaxLevels = { 10, 10, 10, 10, 2 };

        /// <summary>Price of each upgrade's first level: UpgradeCost(n) = C0 × growth^n.</summary>
        public float[] CostBase = { 60f, 90f, 50f, 70f, 900f };

        /// <summary>Price growth per upgrade level (the design range is 1.15–1.25).</summary>
        public float CostGrowth = 1.2f;

        /// <summary>Best level a pilot must have reached before each upgrade can be bought (0 = always).</summary>
        public int[] UnlockLevels = { 0, 0, 0, 2, 4 };

        /// <summary>Max fuel added per Fuel tank level.</summary>
        public float FuelTankPerLevel = 2f;

        /// <summary>Build cost removed per Build efficiency level (fraction of the price).</summary>
        public float EfficiencyPerLevel = 0.04f;

        /// <summary>Starting fuel added per Starting fuel level.</summary>
        public float StartFuelPerLevel = 1f;

        /// <summary>Power-up duration added per level (fraction).</summary>
        public float DurationPerLevel = 0.08f;

        // ------------------------------------------------------------------ adaptive difficulty (index = rating + 2)

        /// <summary>Run speed multiplier at ratings −2 … +2 (values between steps are interpolated).</summary>
        public float[] RatingSpeed = { 0.80f, 0.90f, 1.00f, 1.10f, 1.20f };

        /// <summary>Gaps and layer changes (how tightly chunks are packed).</summary>
        public float[] RatingGaps = { 0.70f, 0.85f, 1.00f, 1.15f, 1.30f };

        /// <summary>Fuel pickups.</summary>
        public float[] RatingFuel = { 1.40f, 1.20f, 1.00f, 0.85f, 0.70f };

        /// <summary>Obstacles (level 5+).</summary>
        public float[] RatingObstacles = { 0.70f, 0.85f, 1.00f, 1.15f, 1.30f };

        /// <summary>Shield and repair drops.</summary>
        public float[] RatingDrops = { 1.40f, 1.20f, 1.00f, 0.85f, 0.70f };

        /// <summary>Lead time of the gap warning on the ghost outline (multiplier of the base lead).</summary>
        public float[] RatingGhostLead = { 1.50f, 1.25f, 1.00f, 0.85f, 0.70f };

        /// <summary>Gem payout multiplier (rises with difficulty, so playing badly on purpose never pays better).</summary>
        public float[] RatingGems = { 0.80f, 0.90f, 1.00f, 1.15f, 1.30f };

        /// <summary>Largest rating change per run.</summary>
        public float RatingStep = 0.25f;

        /// <summary>Runs in the rolling performance average.</summary>
        public int RatingWindow = 5;

        /// <summary>A run that ends before this many seconds counts as an early death.</summary>
        public float EarlyDeathSeconds = 15f;

        /// <summary>Extra rating drop after two early deaths in a row (the rage signal).</summary>
        public float RagePenalty = 0.25f;

        /// <summary>Level an un-upgraded pilot is expected to reach (adaptive performance).</summary>
        public float ExpectedLevelBase = 3f;

        /// <summary>Expected levels added per upgrade level bought.</summary>
        public float ExpectedLevelPerUpgrade = 0.12f;

        // ------------------------------------------------------------------ access

        private static BalanceConfig _current = new BalanceConfig();

        /// <summary>The tuning in use (defaults until the Unity layer loads balance.json). Never null.</summary>
        public static BalanceConfig Current
        {
            get { return _current; }
            set { _current = value != null ? value.Sanitized() : new BalanceConfig(); }
        }

        /// <summary>Price of the next level of an upgrade that is at <paramref name="level"/> now.</summary>
        public long UpgradeCost(UpgradeId id, int level)
        {
            int i = (int)id;
            double c0 = i >= 0 && i < CostBase.Length ? CostBase[i] : 100.0;
            double cost = c0 * Math.Pow(CostGrowth, Math.Max(0, level));
            return (long)Math.Round(Math.Min(cost, 1e15));
        }

        /// <summary>Highest level of an upgrade.</summary>
        public int MaxLevel(UpgradeId id)
        {
            int i = (int)id;
            return i >= 0 && i < MaxLevels.Length ? MaxLevels[i] : 0;
        }

        /// <summary>Best level a pilot must have reached before <paramref name="id"/> can be bought.</summary>
        public int UnlockLevel(UpgradeId id)
        {
            int i = (int)id;
            return i >= 0 && i < UnlockLevels.Length ? UnlockLevels[i] : 0;
        }

        /// <summary>Gems one pickup is worth at <paramref name="level"/> before the adaptive multiplier.</summary>
        public float GemValue(int level)
        {
            return GemValueBase + GemValuePerLevel * Math.Max(0, level - 1);
        }

        /// <summary>
        /// A copy with every array the right length and every number in a sane range, so a hand-edited balance.json can
        /// never break a run (missing entries fall back to the defaults).
        /// </summary>
        public BalanceConfig Sanitized()
        {
            var d = new BalanceConfig();
            var c = (BalanceConfig)MemberwiseClone();
            c.BridgeCost = MathUtil.Clamp(Finite(c.BridgeCost, d.BridgeCost), 0f, 50f);
            c.RampUpCost = MathUtil.Clamp(Finite(c.RampUpCost, d.RampUpCost), 0f, 50f);
            c.RampDownCost = MathUtil.Clamp(Finite(c.RampDownCost, d.RampDownCost), 0f, 50f);
            c.MaxFuel = MathUtil.Clamp(Positive(c.MaxFuel, d.MaxFuel), 1f, 999f);
            c.StartFuel = MathUtil.Clamp(Finite(c.StartFuel, d.StartFuel), 0f, c.MaxFuel);
            c.FuelPickupValue = MathUtil.Clamp(Positive(c.FuelPickupValue, d.FuelPickupValue), 0.1f, 100f);
            c.FillerFuelChance = MathUtil.Clamp01(Finite(c.FillerFuelChance, d.FillerFuelChance));
            c.LateBuildSeconds = MathUtil.Clamp(Finite(c.LateBuildSeconds, d.LateBuildSeconds), 0f, 0.3f);
            c.HullBase = MathUtil.Clamp(c.HullBase, 1, 20);
            c.HullCap = MathUtil.Clamp(c.HullCap, c.HullBase, 30);
            c.GemValueBase = Positive(c.GemValueBase, d.GemValueBase);
            c.GemValuePerLevel = Math.Max(0f, Finite(c.GemValuePerLevel, d.GemValuePerLevel));
            c.GemsPerCell = MathUtil.Clamp(c.GemsPerCell, 1, 4);
            c.OverloadSeconds = Positive(c.OverloadSeconds, d.OverloadSeconds);
            c.OverloadSpeedMultiplier = MathUtil.Clamp(Finite(c.OverloadSpeedMultiplier, d.OverloadSpeedMultiplier), 1f, 2f);
            c.OverloadGrace = MathUtil.Clamp(Finite(c.OverloadGrace, d.OverloadGrace), 0f, 3f);
            c.MagnetSeconds = Positive(c.MagnetSeconds, d.MagnetSeconds);
            c.InstantBuildSeconds = Positive(c.InstantBuildSeconds, d.InstantBuildSeconds);
            c.PowerUpIntervalMin = MathUtil.Clamp(Finite(c.PowerUpIntervalMin, d.PowerUpIntervalMin), 3f, 600f);
            c.PowerUpIntervalMax = MathUtil.Clamp(Finite(c.PowerUpIntervalMax, d.PowerUpIntervalMax), c.PowerUpIntervalMin, 600f);
            c.PowerUpWeights = Fit(c.PowerUpWeights, d.PowerUpWeights);
            c.MaxLevels = Fit(c.MaxLevels, d.MaxLevels);
            c.CostBase = Fit(c.CostBase, d.CostBase);
            c.UnlockLevels = Fit(c.UnlockLevels, d.UnlockLevels);
            c.CostGrowth = MathUtil.Clamp(Finite(c.CostGrowth, d.CostGrowth), 1f, 3f);
            c.FuelTankPerLevel = Math.Max(0f, Finite(c.FuelTankPerLevel, d.FuelTankPerLevel));
            c.EfficiencyPerLevel = MathUtil.Clamp(Finite(c.EfficiencyPerLevel, d.EfficiencyPerLevel), 0f, 0.09f);
            c.StartFuelPerLevel = Math.Max(0f, Finite(c.StartFuelPerLevel, d.StartFuelPerLevel));
            c.DurationPerLevel = Math.Max(0f, Finite(c.DurationPerLevel, d.DurationPerLevel));
            c.RatingSpeed = Fit(c.RatingSpeed, d.RatingSpeed);
            c.RatingGaps = Fit(c.RatingGaps, d.RatingGaps);
            c.RatingFuel = Fit(c.RatingFuel, d.RatingFuel);
            c.RatingObstacles = Fit(c.RatingObstacles, d.RatingObstacles);
            c.RatingDrops = Fit(c.RatingDrops, d.RatingDrops);
            c.RatingGhostLead = Fit(c.RatingGhostLead, d.RatingGhostLead);
            c.RatingGems = Fit(c.RatingGems, d.RatingGems);
            c.RatingStep = MathUtil.Clamp(Finite(c.RatingStep, d.RatingStep), 0f, 1f);
            c.RatingWindow = MathUtil.Clamp(c.RatingWindow, 1, 20);
            c.EarlyDeathSeconds = Math.Max(0f, Finite(c.EarlyDeathSeconds, d.EarlyDeathSeconds));
            c.RagePenalty = MathUtil.Clamp(Finite(c.RagePenalty, d.RagePenalty), 0f, 1f);
            c.ExpectedLevelBase = MathUtil.Clamp(Finite(c.ExpectedLevelBase, d.ExpectedLevelBase), 1f, 100f);
            c.ExpectedLevelPerUpgrade = MathUtil.Clamp(Finite(c.ExpectedLevelPerUpgrade, d.ExpectedLevelPerUpgrade), 0f, 5f);
            return c;
        }

        private static float Finite(float value, float fallback)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? fallback : value;
        }

        private static float Positive(float value, float fallback)
        {
            return value > 0f && !float.IsNaN(value) && !float.IsInfinity(value) ? value : fallback;
        }

        private static float[] Fit(float[] value, float[] fallback)
        {
            var result = (float[])fallback.Clone();
            if (value != null)
            {
                for (int i = 0; i < result.Length && i < value.Length; i++)
                {
                    if (!float.IsNaN(value[i]) && !float.IsInfinity(value[i]) && value[i] >= 0f)
                    {
                        result[i] = value[i];
                    }
                }
            }

            return result;
        }

        private static int[] Fit(int[] value, int[] fallback)
        {
            var result = (int[])fallback.Clone();
            if (value != null)
            {
                for (int i = 0; i < result.Length && i < value.Length; i++)
                {
                    if (value[i] >= 0)
                    {
                        result[i] = value[i];
                    }
                }
            }

            return result;
        }
    }
}

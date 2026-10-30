using System;

namespace BeamRunner.Core.Balance
{
    /// <summary>The upgrades bought with gems (values index <see cref="BalanceConfig"/> arrays and the save's levels).</summary>
    public enum UpgradeId
    {
        /// <summary>Higher max fuel.</summary>
        FuelTank = 0,

        /// <summary>Lower fuel cost per piece.</summary>
        Efficiency,

        /// <summary>More fuel at run start.</summary>
        StartFuel,

        /// <summary>Longer instant build, Overload and magnet.</summary>
        Duration,

        /// <summary>+1 max hull (capped).</summary>
        Hull,
    }

    /// <summary>
    /// The craft for one run, computed from the pilot's upgrade levels (<see cref="FromLevels"/>): its fuel tank, starting
    /// fuel, build prices, power-up durations and hull.
    /// </summary>
    [Serializable]
    public sealed class ShipStats
    {
        public const int UpgradeCount = 5;

        public float MaxFuel = 20f;
        public float StartFuel = 10f;
        public float BridgeCost = 2f;
        public float RampUpCost = 3f;
        public float RampDownCost = 1f;

        /// <summary>Multiplier of every timed power-up.</summary>
        public float DurationScale = 1f;

        public int MaxHull = 3;

        /// <summary>Upgrade levels bought in total (drives the adaptive expectation).</summary>
        public int Power;

        /// <summary>Fuel a build costs.</summary>
        public float Cost(Field.BuildKind kind)
        {
            switch (kind)
            {
                case Field.BuildKind.Bridge:
                    return BridgeCost;
                case Field.BuildKind.RampUp:
                    return RampUpCost;
                case Field.BuildKind.RampDown:
                    return RampDownCost;
                default:
                    return 0f;
            }
        }

        /// <summary>The craft of a pilot with these upgrade levels (indexed by <see cref="UpgradeId"/>; null = none).</summary>
        public static ShipStats FromLevels(int[] levels, BalanceConfig b = null)
        {
            b = b ?? BalanceConfig.Current;
            int tank = Level(levels, UpgradeId.FuelTank, b);
            int efficiency = Level(levels, UpgradeId.Efficiency, b);
            int start = Level(levels, UpgradeId.StartFuel, b);
            int duration = Level(levels, UpgradeId.Duration, b);
            int hull = Level(levels, UpgradeId.Hull, b);
            float costScale = Math.Max(0.1f, 1f - b.EfficiencyPerLevel * efficiency);
            float maxFuel = b.MaxFuel + b.FuelTankPerLevel * tank;
            return new ShipStats
            {
                MaxFuel = maxFuel,
                StartFuel = Math.Min(maxFuel, b.StartFuel + b.StartFuelPerLevel * start),
                BridgeCost = b.BridgeCost * costScale,
                RampUpCost = b.RampUpCost * costScale,
                RampDownCost = b.RampDownCost * costScale,
                DurationScale = 1f + b.DurationPerLevel * duration,
                MaxHull = Math.Min(b.HullCap, b.HullBase + hull),
                Power = tank + efficiency + start + duration + hull,
            };
        }

        private static int Level(int[] levels, UpgradeId id, BalanceConfig b)
        {
            int i = (int)id;
            int level = levels != null && i < levels.Length ? levels[i] : 0;
            return MathUtil.Clamp(level, 0, b.MaxLevel(id));
        }

        public ShipStats Clone()
        {
            return (ShipStats)MemberwiseClone();
        }
    }
}

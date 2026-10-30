using System;
using BeamRunner.Core.Balance;
using BeamRunner.Core.Field;

namespace BeamRunner.Core.Sim
{
    /// <summary>Player input for one simulation step (one rendered frame). +X = right.</summary>
    public struct RunInput
    {
        /// <summary>Lanes to move this frame: −1 per swipe/press left, +1 per swipe/press right (queued, clamped).</summary>
        public int LaneShift;

        /// <summary>What to build on the next tile this frame (tap = bridge, swipe up/down = ramps).</summary>
        public BuildKind Build;

        /// <summary>
        /// When true, <see cref="AbsoluteLane"/> replaces the target lane this frame (bots, tests). Clamped to the field.
        /// </summary>
        public bool UseAbsoluteLane;

        public int AbsoluteLane;
    }

    /// <summary>Parameters for a single run.</summary>
    public sealed class RunConfig
    {
        public int Seed;

        /// <summary>First-ever run: roomier levels and coaching hints.</summary>
        public bool Tutorial;

        /// <summary>Score bonus from pilot rank, e.g. 0.12 = +12 %.</summary>
        public float ScoreBonus;

        /// <summary>Number of revives allowed (usually 1: rewarded ad or gems).</summary>
        public int MaxRevives = 1;

        /// <summary>Level the run starts at (1-based; later starts are unlocked by reaching them).</summary>
        public int StartLevel = 1;

        /// <summary>The craft (from the pilot's upgrades). Null = no upgrades.</summary>
        public ShipStats Ship;

        /// <summary>Adaptive difficulty for this run (fixed at run start). Null = Standard.</summary>
        public DifficultyModifiers Modifiers;

        /// <summary>Tuning; null = <see cref="BalanceConfig.Current"/>.</summary>
        public BalanceConfig Balance;

        /// <summary>Nothing ever crashes (footage runs, the menu background): falls land, obstacles shatter.</summary>
        public bool Invincible;

        /// <summary>The pilot's hidden rating moves after this run (normal runs; not the tutorial or the Daily Run).</summary>
        public bool Adaptive;

        /// <summary>Checkpoints continue on their own after a short pause (bots, the menu background, footage).</summary>
        public bool AutoCheckpoint;
    }

    /// <summary>Why a run ended.</summary>
    public enum CrashCause
    {
        /// <summary>Rode (or built) into an obstacle.</summary>
        Obstacle = 0,

        /// <summary>Built a ramp up from the top layer.</summary>
        Ceiling,

        /// <summary>Fell off (or built down from) the bottom layer.</summary>
        Void,

        /// <summary>Fell two layers.</summary>
        LongFall,

        /// <summary>A one-layer fall took the last hull point.</summary>
        HullBroken,
    }

    /// <summary>A one-time boost picked at a level wall checkpoint (lasts the rest of the run).</summary>
    public enum CheckpointPerk
    {
        None = 0,

        /// <summary>Fuel refilled to the top.</summary>
        Refuel,

        /// <summary>One hull point back.</summary>
        Patch,

        /// <summary>A shield.</summary>
        Shield,

        /// <summary>Builds cost 15 % less for the rest of the run.</summary>
        Efficiency,

        /// <summary>+5 max fuel for the rest of the run.</summary>
        BigTank,

        /// <summary>Power-ups last 25 % longer for the rest of the run.</summary>
        LongerBoosts,
    }

    /// <summary>Summary of a finished (or in-progress) run, consumed by missions, stats and the results screen.</summary>
    [Serializable]
    public sealed class RunResult
    {
        public long Score;

        /// <summary>Metres flown in this run.</summary>
        public float Distance;

        /// <summary>Gems collected (already worth their level and difficulty value).</summary>
        public int Gems;

        /// <summary>Last-second builds and last-moment obstacle dodges (see <see cref="FieldSpec.NearMissWindow"/>).</summary>
        public int NearMisses;

        /// <summary>Longest chain of useful builds, full trails and near misses without taking damage.</summary>
        public int BestNearMissChain;

        /// <summary>Pieces built (bridges + ramps).</summary>
        public int Builds;

        public int Bridges;
        public int RampsUp;
        public int RampsDown;

        /// <summary>Gaps the craft crossed on something it built.</summary>
        public int GapsBridged;

        /// <summary>Fuel collected and spent; fuel spent on tiles that already had the same rail (wasted).</summary>
        public float FuelCollected;

        public float FuelSpent;
        public float FuelWasted;

        /// <summary>Builds tried with too little fuel.</summary>
        public int EmptyBuilds;

        /// <summary>One-layer falls survived, and hull points lost.</summary>
        public int Falls;

        public int DamageTaken;

        /// <summary>Gem lines collected in full (each paid the full-trail bonus).</summary>
        public int FullTrails;

        /// <summary>Obstacles passed in a neighbouring lane or layer.</summary>
        public int CubesDodged;

        /// <summary>Obstacles broken by Overload (or a shield).</summary>
        public int Smashed;

        public int Overloads;
        public int MaxMultiplier;

        /// <summary>Level the run started at and the highest level reached (1-based).</summary>
        public int StartLevel;

        public int LevelReached;

        /// <summary>0..1 progress through the last level reached.</summary>
        public float LevelProgress;

        /// <summary>Levels finished in this run (walls climbed).</summary>
        public int LevelsCleared;

        /// <summary>Seconds ridden on the top layer.</summary>
        public float TopLayerSeconds;

        public float Duration;
        public int PowerUps;
        public int ShieldsBroken;
        public int Repairs;
        public bool Revived;
        public int Seed;

        /// <summary>How the run ended (meaningful once it has).</summary>
        public CrashCause Cause;

        /// <summary>Hidden skill rating the run was played at.</summary>
        public float Rating;

        /// <summary>Copied from <see cref="RunConfig.Adaptive"/>.</summary>
        public bool Adaptive;

        public RunResult Clone()
        {
            return (RunResult)MemberwiseClone();
        }
    }
}

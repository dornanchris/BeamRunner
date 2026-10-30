namespace BeamRunner.Core.Sim
{
    /// <summary>Something that happened during a simulation step, for the presentation layer to react to.</summary>
    public enum RunEventType
    {
        /// <summary>A last-second build or a last-moment dodge. Count = new chain length, X/Y/Z = where.</summary>
        NearMiss = 0,

        /// <summary>Overload broke an obstacle. Id = tile, Count = cell index, X/Y/Z = the cube's centre.</summary>
        Smashed,

        PickupSpawned,

        /// <summary>Id = pickup id, Count = (int)PickupKind, Value = gems or fuel it was worth.</summary>
        PickupCollected,

        PickupDespawned,

        /// <summary>Overload started (or was refreshed). Value = seconds.</summary>
        OverloadStarted,

        OverloadEnded,
        ShieldGained,

        /// <summary>The shield absorbed a fall, a ceiling pinch or a crash. Count = (int)CrashCause it saved.</summary>
        ShieldBroken,

        MagnetStarted,
        MagnetEnded,

        /// <summary>Count = new multiplier.</summary>
        MultiplierChanged,

        /// <summary>Count = level number (1-based) now starting.</summary>
        LevelStarted,

        /// <summary>Count = level number just finished, Value = bonus points awarded.</summary>
        LevelCleared,

        /// <summary>X/Y/Z = crash location, Count = (int)CrashCause.</summary>
        Crashed,

        Revived,

        /// <summary>Count = (int)TutorialHint.</summary>
        TutorialHint,

        /// <summary>Score bonus popup. Count = points, Value = multiplier applied.</summary>
        Bonus,

        /// <summary>Every gem of a line collected. Id = trail id, Count = points awarded, Value = gems in the line.</summary>
        TrailCompleted,

        /// <summary>A timed power-up started (or was refreshed). Count = (int)PickupKind, Value = seconds.</summary>
        PowerUpStarted,

        /// <summary>A timed power-up ran out. Count = (int)PickupKind.</summary>
        PowerUpEnded,

        /// <summary>The craft started switching lanes. Count = target lane, Value = direction (−1 left, +1 right).</summary>
        LaneChanged,

        /// <summary>
        /// A piece snapped into place. Id = tile, Count = (int)BuildKind, Value = fuel it cost, X/Y/Z = the cell's start
        /// (lane centre, rail height, tile start).
        /// </summary>
        Built,

        /// <summary>A build was refused for lack of fuel (red flash + "empty" sound). Count = (int)BuildKind.</summary>
        BuildFailed,

        /// <summary>The craft left its rail: Count = layer it fell from.</summary>
        FellOff,

        /// <summary>A one-layer fall ended on a rail. Count = hull left, Value = 1 if the shield took it.</summary>
        Landed,

        /// <summary>The hull lost a point. Count = hull left.</summary>
        Damaged,

        /// <summary>The hull was restored. Count = hull now.</summary>
        Repaired,

        /// <summary>The craft reached the wall that ends the level and starts climbing it. Count = level.</summary>
        WallReached,

        /// <summary>At the top of the wall: the checkpoint (stats, perk pick). Count = level just cleared.</summary>
        CheckpointReached,

        /// <summary>A checkpoint perk was applied. Count = (int)CheckpointPerk.</summary>
        PerkApplied,

        /// <summary>The craft changed layer by riding a ramp. Count = new layer, Value = direction.</summary>
        LayerChanged,

        /// <summary>Fuel fell to (or under) the price of a bridge.</summary>
        FuelLow,
    }

    /// <summary>First-run coaching prompts.</summary>
    public enum TutorialHint
    {
        /// <summary>Swipe left or right to change lanes.</summary>
        Move = 0,

        /// <summary>A gap ahead: tap to bridge it.</summary>
        Bridge,

        /// <summary>Swipe up or down to build a ramp.</summary>
        Ramps,

        /// <summary>Builds cost fuel: collect fuel cells.</summary>
        Fuel,

        /// <summary>Tutorial over.</summary>
        Complete,

        /// <summary>The ceiling and the void kill.</summary>
        Edges,

        /// <summary>Obstacles: change lane or layer.</summary>
        Obstacles,
    }

    /// <summary>A simulation event. Plain struct; the simulation reuses its list every frame.</summary>
    public struct RunEvent
    {
        public RunEventType Type;
        public int Id;
        public int Count;
        public float Value;
        public float X;
        public float Y;
        public double Z;

        public RunEvent(RunEventType type, int id = 0, int count = 0, float value = 0f, float x = 0f, float y = 0f,
            double z = 0)
        {
            Type = type;
            Id = id;
            Count = count;
            Value = value;
            X = x;
            Y = y;
            Z = z;
        }

        public override string ToString()
        {
            return Type + " id=" + Id + " count=" + Count + " value=" + Value;
        }
    }
}

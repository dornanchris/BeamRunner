namespace BeamRunner.Core.Field
{
    /// <summary>Kinds of collectible. Values are saved in events and index colour tables: new kinds go at the end.</summary>
    public enum PickupKind
    {
        /// <summary>Currency (worth <see cref="Pickup.Value"/> gems). Small diamond; the richest trails run on the top layer.</summary>
        Gem = 0,

        /// <summary>Blocks one fall, ceiling pinch or crash, until used.</summary>
        Shield,

        /// <summary>Pulls in gems and fuel for a few seconds.</summary>
        Magnet,

        /// <summary>Overload: a speed burst that breaks through obstacles and flies over gaps.</summary>
        Overload,

        /// <summary>Fuel (worth <see cref="Pickup.Value"/> fuel): pays for building.</summary>
        Fuel,

        /// <summary>Restores the hull to full (rare, on risky routes).</summary>
        Repair,

        /// <summary>Building is free for a few seconds.</summary>
        InstantBuild,
    }

    /// <summary>A collectible floating above a rail (or above a gap, where only a build reaches it).</summary>
    public sealed class Pickup
    {
        public int Id;

        public PickupKind Kind;

        /// <summary>Track position. Moves when attracted by the magnet.</summary>
        public double Z;

        /// <summary>Position on X/Y. Moves when attracted by the magnet.</summary>
        public float X;

        public float Y;

        /// <summary>Lane and layer it was placed on.</summary>
        public int Lane;

        public int Layer;

        /// <summary>Gems (gems) or fuel units (fuel) this pickup is worth; already scaled by level and difficulty.</summary>
        public int Value = 1;

        /// <summary>The gem trail this gem belongs to (null = none): collecting all of it pays the full-trail bonus.</summary>
        public GemTrail Group;

        public bool Attracted;

        public bool Collected;

        /// <summary>True for gems and fuel (the magnet pulls them; small pickup radius).</summary>
        public bool IsResource
        {
            get { return Kind == PickupKind.Gem || Kind == PickupKind.Fuel; }
        }
    }

    /// <summary>A line of gems that reads as one trail; collected in full it pays a bonus.</summary>
    public sealed class GemTrail
    {
        public int Id;

        /// <summary>Gems in the trail.</summary>
        public int Size;

        /// <summary>Gems of it collected so far.</summary>
        public int Collected;

        /// <summary>One of its gems was missed: no bonus.</summary>
        public bool Broken;
    }
}

namespace BeamRunner.Core.Field
{
    /// <summary>
    /// The theme of a level: which chunk tags it favours (see <see cref="LevelSpec"/> weights) and its banner name.
    /// Values index the name table in <see cref="Levels"/>: new kinds go at the end.
    /// </summary>
    public enum LevelKind
    {
        /// <summary>Gaps on one layer, plenty of fuel: tap to bridge.</summary>
        Bridges = 0,

        /// <summary>Ramps between layers and gem trails that dip and climb: swipe up and down to build.</summary>
        Ramps,

        /// <summary>The ceiling and the void, introduced next to a shield: the edges kill.</summary>
        Edges,

        /// <summary>Tighter fuel and longer gaps: every build matters.</summary>
        FuelLine,

        /// <summary>Cube walls on the rails force a new route.</summary>
        Blockade,

        /// <summary>Routes that weave between the layers around obstacles.</summary>
        Switchback,

        /// <summary>Rich, gappy top-layer runs under the ceiling.</summary>
        Skyline,

        /// <summary>Everything, mixed.</summary>
        Gauntlet,

        /// <summary>Everything, mixed, at the densest spacing.</summary>
        Overload,
    }
}

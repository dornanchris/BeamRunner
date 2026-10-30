using System.Collections.Generic;

namespace BeamRunner.Core.Field
{
    /// <summary>
    /// Every hand-built chunk (see <see cref="Chunk"/> for the notation: rows are tiles, groups are the bottom, middle and
    /// top layer, lanes left to right). Tier 0 teaches bridging on the lower two layers (level 1); tier 1 opens the top
    /// layer, ramps and the edges (levels 2–3); tier 2 adds long gaps and obstacles (levels 4–7, obstacles from 5); tier 3
    /// mixes everything. <c>ChunkTests</c> checks every chunk parses, is survivable without a fall from at least one layer
    /// with the starting fuel, and is mirror-safe. Add chunks here.
    /// </summary>
    public static class ChunkLibrary
    {
        private static List<Chunk> _all;

        public static IReadOnlyList<Chunk> All
        {
            get { return _all ?? (_all = Build()); }
        }

        private static List<Chunk> Build()
        {
            return new List<Chunk>
            {
                // ------------------------------------------------------------ tier 0: bridges (no top layer)
                new Chunk("t0_gap", ChunkTag.Bridge, 0,
                    "=== === ..., === ... ..., === === ..., ... === ..."),
                new Chunk("t0_gap_gem", ChunkTag.Gems, 0,
                    "=== =g= ..., === .*. ..., === =g= ..., .=. =g= ..."),
                new Chunk("t0_side_gaps", ChunkTag.Bridge, 0,
                    "=== === ..., === .=. ..., =.= .g. ..., === === ..."),
                new Chunk("t0_double", ChunkTag.Bridge, 0,
                    "=== =f= ..., === ... ..., === ... ..., === === ..., =.= === ..."),
                new Chunk("t0_fuel", ChunkTag.Fuel, 0,
                    "=== === ..., === fff ..., === === ..., === f=f ..."),
                new Chunk("t0_stagger", ChunkTag.Bridge, 0,
                    "=== === ..., === .== ..., === ==. ..., =.= =.= ..., === === ..."),
                new Chunk("t0_net_hole", ChunkTag.Bridge, 0,
                    "=== === ..., ... =f= ..., =.= === ..., === ... ..., === === ..."),
                new Chunk("t0_trail", ChunkTag.Gems, 0,
                    "=== =g= ..., === .*. ..., === =g= ..., === .*. ..., === =g= ..."),
                new Chunk("t0_lane_trail", ChunkTag.Gems, 0,
                    "=== g== ..., === *.. ..., === g.. ..., === *.= ..., === g== ..."),

                // ------------------------------------------------------------ tier 1: ramps, the top layer, the edges
                new Chunk("t1_ride_up", ChunkTag.Ramp, 1,
                    "=== =^= ..., === === ggg, === =.= ggg, === === =v=, === === ..."),
                new Chunk("t1_build_up", ChunkTag.Ramp, 1,
                    "=== =f= ..., === === ..., === === ggg, === === g*g, === === ggg, === === ..."),
                new Chunk("t1_ride_down", ChunkTag.Ramp, 1,
                    "=== === ===, =.= =v= ===, === ... ===, =g= ... =f=, === === ==="),
                new Chunk("t1_dip", ChunkTag.Gems, 1,
                    "=== =g= ..., =g= === ..., =g= === ..., =g= === ..., === =g= ..."),
                new Chunk("t1_ceiling", ChunkTag.Edge, 1,
                    "=== === ===, === === =s=, === .=. g=g, === .=. g.g, === === g=g, === === ==="),
                new Chunk("t1_void", ChunkTag.Edge, 1,
                    "=== === ..., .=. =.= ..., .=. =.= ..., =f= === ..., === === ..."),
                new Chunk("t1_fuel_top", ChunkTag.Fuel, 1,
                    "=== === ===, === =f= =F=, === === f=f, === === ==="),
                new Chunk("t1_gap_row", ChunkTag.Bridge, 1,
                    "=== === ..., =.= ... ..., =.= *** ..., === === ..."),
                new Chunk("t1_top_trail", ChunkTag.Gems, 1,
                    "=== === ===, === === =g=, === =.= .*., === === =g=, === === .*., === === =g="),
                new Chunk("t1_climb", ChunkTag.Ramp, 1,
                    "=^= === ..., ... === ..., =f= =^= ..., === ... =g=, === === =g=, === === ..."),
                new Chunk("t1_shield_void", ChunkTag.Edge, 1,
                    "=== =s= ..., ... === ..., .=. === ..., ... =.= ..., === === ..."),

                // ------------------------------------------------------------ tier 2: long gaps, obstacles, scarcer fuel
                new Chunk("t2_long_gap", ChunkTag.Bridge, 2,
                    "=== === ===, === ... =f=, === *.* ===, === ... =f=, === === ==="),
                new Chunk("t2_stagger_all", ChunkTag.Bridge, 2,
                    "=== === ===, =.= .== ==., === ==. .==, .== =.= ===, === === ==="),
                new Chunk("t2_blocks", ChunkTag.Obstacle, 2,
                    "=== === ===, === #== ===, === === ==#, #== =#= ===, === === ==="),
                new Chunk("t2_wall_mid", ChunkTag.Obstacle, 2,
                    "=== === ===, === === =f=, === ### ===, === === ===, === === ==="),
                new Chunk("t2_wall_low", ChunkTag.Obstacle, 2,
                    "=== === ===, === === =f=, #=# ### ===, === === ===, === === ==="),
                new Chunk("t2_top_risk", ChunkTag.Gems, 2,
                    "=== === ===, === === g.g, === =.= *g*, === === g.g, === === =r=, === === ..."),
                new Chunk("t2_weave", ChunkTag.Ramp, 2,
                    "=== =^= ..., === ... =g=, === ... =v=, =^= =g= ..., ... === ..., === === ..."),
                new Chunk("t2_split", ChunkTag.Edge, 2,
                    "=== === ===, ... =#= g=g, ... =.= g=g, === =.= ===, === === ==="),
                new Chunk("t2_long_void", ChunkTag.Edge, 2,
                    "=== === ..., ... === ..., ... .=. ..., ... === ..., =f= =.= ..., === === ..."),
                new Chunk("t2_fuel_detour", ChunkTag.Fuel, 2,
                    "=== === ===, =f= ... ===, =f= ... =o=, =f= ... ===, === === ==="),
                new Chunk("t2_slalom", ChunkTag.Obstacle, 2,
                    "=== === ===, =o= #=# ===, === === o=o, #=# =o= ===, === === ==="),

                // ------------------------------------------------------------ tier 3: everything
                new Chunk("t3_gauntlet", ChunkTag.Bridge, 3,
                    "=== === ===, .=. =.= .=., =.= .=. =.=, .=. =.= .=., === === ==="),
                new Chunk("t3_tower", ChunkTag.Ramp, 3,
                    "=^= === ..., ... =^= ..., ... ... =g=, ... ... *g*, =f= ... =v=, === =v= ..., === ... ..., === === ..."),
                new Chunk("t3_cage", ChunkTag.Obstacle, 3,
                    "=== === ===, #=# ### #=#, === === ===, ### #=# ###, === === ==="),
                new Chunk("t3_sky_run", ChunkTag.Edge, 3,
                    "=== === ===, === === g*g, .=. =.= *g*, ... .=. g*g, .=. === *p*, === === g=g, === === ==="),
                new Chunk("t3_squeeze", ChunkTag.Obstacle, 3,
                    "=== === ===, ==# #== o=o, #== ==# ===, ==# #o# =o=, === === ==="),
                new Chunk("t3_fuel_gamble", ChunkTag.Fuel, 3,
                    "=== === ===, === === .F., === =f= .F., =#= === .F., === === ==="),
            };
        }
    }
}

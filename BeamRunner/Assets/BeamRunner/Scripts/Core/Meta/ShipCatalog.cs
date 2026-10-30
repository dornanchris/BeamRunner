using System;
using System.Collections.Generic;
using BeamRunner.Core.Localization;

namespace BeamRunner.Core.Meta
{
    /// <summary>
    /// The requirement a ship is gated behind. A ship becomes available once its requirement is met and is then bought
    /// for its <see cref="ShipDefinition.Price"/> (a price of 0 unlocks it automatically when the requirement is met).
    /// </summary>
    public enum UnlockKind
    {
        /// <summary>Owned from the start.</summary>
        Free = 0,

        /// <summary>No requirement: just the gem price.</summary>
        Gems,

        /// <summary>Reach a pilot rank.</summary>
        Rank,

        /// <summary>A best single-run distance (metres).</summary>
        Distance,

        /// <summary>A best single-run score (points).</summary>
        Score,

        /// <summary>Fly the Daily Run on this many different UTC days (<see cref="DailyRunState.DaysPlayed"/>; not consecutive).</summary>
        DailyRunDays,

        /// <summary>Reach a level in any run (<see cref="PlayerStats.BestLevel"/>).</summary>
        Level,
    }

    /// <summary>
    /// A ship in the hangar. Ships are cosmetic (identical handling) so the game stays fair; each has a
    /// distinct procedural silhouette (<see cref="Design"/>) and livery.
    /// </summary>
    public sealed class ShipDefinition
    {
        public readonly string Id;

        /// <summary>String-table key of the name (ship names are proper names: kept in Latin letters in every language).</summary>
        public readonly string NameKey;

        /// <summary>String-table key of the one-line tagline.</summary>
        public readonly string TaglineKey;

        /// <summary>Procedural mesh design id: arrow, manta, viper, halo, phantom, nova, shuttle.</summary>
        public readonly string Design;

        public readonly ColorRGB Body;
        public readonly ColorRGB Glow;
        public readonly ColorRGB Trail;
        public readonly UnlockKind Unlock;

        /// <summary>Requirement target: pilot rank, metres, points or Daily Run days, depending on <see cref="Unlock"/> (0 for Free/Gems).</summary>
        public readonly int UnlockValue;

        /// <summary>Gems to pay once the requirement is met (0 = unlocked automatically when it is met).</summary>
        public readonly int Price;

        public ShipDefinition(string id, string nameKey, string taglineKey, string design, string body, string glow,
            string trail, UnlockKind unlock, int unlockValue, int price)
        {
            Id = id;
            NameKey = nameKey;
            TaglineKey = taglineKey;
            Design = design;
            Body = ColorRGB.Hex(body);
            Glow = ColorRGB.Hex(glow);
            Trail = ColorRGB.Hex(trail);
            Unlock = unlock;
            UnlockValue = unlock == UnlockKind.Free || unlock == UnlockKind.Gems ? 0 : unlockValue;
            Price = unlock == UnlockKind.Free ? 0 : Math.Max(0, price);
        }

        /// <summary>True when something other than gems gates the ship (rank, distance, score, Daily Run days).</summary>
        public bool HasRequirement
        {
            get { return Unlock != UnlockKind.Free && Unlock != UnlockKind.Gems; }
        }

        /// <summary>Display name in the current language (upper-case, e.g. "MANTA").</summary>
        public string DisplayName
        {
            get { return Loc.T(NameKey); }
        }

        /// <summary>Same as <see cref="DisplayName"/>.</summary>
        public string Name
        {
            get { return Loc.T(NameKey); }
        }

        /// <summary>Tagline in the current language.</summary>
        public string Tagline
        {
            get { return Loc.T(TaglineKey); }
        }

        /// <summary>
        /// The ship's requirement in the current language ("REACH PILOT RANK 10"); for a gems-only ship its price
        /// ("7,500 GEMS").
        /// </summary>
        public string UnlockDescription
        {
            get
            {
                switch (Unlock)
                {
                    case UnlockKind.Free:
                        return Loc.T("ship.unlock_starter");
                    case UnlockKind.Gems:
                        return Loc.T("ship.unlock_gems", ("n", Price));
                    case UnlockKind.Rank:
                        return Loc.T("ship.unlock_rank", ("rank", UnlockValue));
                    case UnlockKind.Distance:
                        return Loc.T("ship.unlock_distance", ("n", UnlockValue));
                    case UnlockKind.Score:
                        return Loc.T("ship.unlock_score", ("n", UnlockValue));
                    case UnlockKind.DailyRunDays:
                        return Loc.T("ship.unlock_daily_days", ("n", UnlockValue));
                    case UnlockKind.Level:
                        return Loc.T("ship.unlock_level", ("n", UnlockValue));
                    default:
                        return string.Empty;
                }
            }
        }
    }

    /// <summary>
    /// All ships, in hangar order. Unlocking the whole hangar is meant to take a committed player (about ten minutes a
    /// day) well over a month: prices are set against measured income (docs/ARCHITECTURE.md, "Ship unlock pacing"),
    /// and the Daily Run gates make the calendar part impossible to shortcut with gems or purchases.
    /// </summary>
    public static class ShipCatalog
    {
        public const string DefaultShipId = "arrow";

        private static readonly ShipDefinition[] Ships =
        {
            new ShipDefinition("arrow", "ship.arrow", "ship.arrow_tagline", "arrow",
                "#1B1F3B", "#00E5FF", "#00E5FF", UnlockKind.Free, 0, 0),
            new ShipDefinition("shuttle", "ship.shuttle", "ship.shuttle_tagline", "shuttle",
                "#DDE3F0", "#FF2BD6", "#FF2BD6", UnlockKind.Free, 0, 0),

            // Hangar order = the order an average player (about 10 minutes a day) unlocks them, one every few days;
            // the day each lands is in docs/ARCHITECTURE.md "Ship unlock pacing". Skins reuse a hull design with a new
            // livery; the Nova hull is kept for the flagship.
            new ShipDefinition("manta", "ship.manta", "ship.manta_tagline", "manta",
                "#1A0F2E", "#FF2BD6", "#FF7AE6", UnlockKind.Gems, 0, 3000),
            new ShipDefinition("spark", "ship.spark", "ship.spark_tagline", "arrow",
                "#10202A", "#FFD400", "#FFF27A", UnlockKind.Level, 4, 3500),
            new ShipDefinition("viper", "ship.viper", "ship.viper_tagline", "viper",
                "#0E2016", "#7CFF3A", "#C6FF6B", UnlockKind.Score, 25000, 4500),
            new ShipDefinition("riptide", "ship.riptide", "ship.riptide_tagline", "manta",
                "#04161F", "#00FFC8", "#00B3FF", UnlockKind.Rank, 5, 5000),
            new ShipDefinition("halo", "ship.halo", "ship.halo_tagline", "halo",
                "#1C1A08", "#FFE45C", "#FFB300", UnlockKind.DailyRunDays, 7, 5500),
            new ShipDefinition("venom", "ship.venom", "ship.venom_tagline", "viper",
                "#150A1C", "#B6FF00", "#D400FF", UnlockKind.Level, 8, 6500),
            new ShipDefinition("eclipse", "ship.eclipse", "ship.eclipse_tagline", "halo",
                "#050507", "#FF1744", "#FF6A3D", UnlockKind.DailyRunDays, 14, 7000),
            new ShipDefinition("phantom", "ship.phantom", "ship.phantom_tagline", "phantom",
                "#08080C", "#9D4DFF", "#6A5CFF", UnlockKind.Rank, 10, 8000),
            new ShipDefinition("wraith", "ship.wraith", "ship.wraith_tagline", "phantom",
                "#D8E6F0", "#7FDBFF", "#FFFFFF", UnlockKind.Score, 60000, 9000),
            new ShipDefinition("aurora", "ship.aurora", "ship.aurora_tagline", "viper",
                "#06140F", "#38FF9C", "#7A5CFF", UnlockKind.DailyRunDays, 21, 10000),
            new ShipDefinition("solaris", "ship.solaris", "ship.solaris_tagline", "arrow",
                "#F2E6C9", "#FF9E1B", "#FFD36B", UnlockKind.DailyRunDays, 25, 11500),

            // The flagship: a month of Daily Runs and about a month of gems.
            new ShipDefinition("nova", "ship.nova", "ship.nova_tagline", "nova",
                "#2A0A04", "#FF6A00", "#FF2E4D", UnlockKind.DailyRunDays, 30, 36000),
        };

        public static IReadOnlyList<ShipDefinition> All
        {
            get { return Ships; }
        }

        /// <summary>Finds a ship by id; returns the default ship if the id is unknown.</summary>
        public static ShipDefinition Get(string id)
        {
            for (int i = 0; i < Ships.Length; i++)
            {
                if (Ships[i].Id == id)
                {
                    return Ships[i];
                }
            }

            return Ships[0];
        }

        public static bool Exists(string id)
        {
            for (int i = 0; i < Ships.Length; i++)
            {
                if (Ships[i].Id == id)
                {
                    return true;
                }
            }

            return false;
        }
    }
}

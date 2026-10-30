namespace BeamRunner.Services
{
    /// <summary>
    /// Logical in-app product ids. They are used verbatim as the store product ids in Google Play Console and
    /// App Store Connect (lowercase letters, digits, dots and underscores are valid on both stores).
    /// The matching catalogue entries (type, gem amount, fallback price) live in
    /// <c>Resources/BeamRunner/Config/monetization.json</c>.
    /// </summary>
    public static class ProductIds
    {
        /// <summary>Non-consumable: removes interstitial ads forever. Rewarded ads stay available (opt-in).</summary>
        public const string RemoveAds = "beamrunner.removeads";

        /// <summary>Consumable: 1,000 gems.</summary>
        public const string GemsSmall = "beamrunner.gems.small";

        /// <summary>Consumable: 6,000 gems.</summary>
        public const string GemsMedium = "beamrunner.gems.medium";

        /// <summary>Consumable: 15,000 gems.</summary>
        public const string GemsLarge = "beamrunner.gems.large";

        /// <summary>Returns a new array with every built-in product id (shop display order).</summary>
        public static string[] All()
        {
            return new[] { RemoveAds, GemsSmall, GemsMedium, GemsLarge };
        }
    }
}

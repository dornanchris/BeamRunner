using BeamRunner.Core.Localization;

namespace BeamRunner.Services
{
    /// <summary>
    /// Localized in-game names and descriptions of the store products. The built-in products (<see cref="ProductIds"/>)
    /// use the string tables; a product added only in monetization.json shows the config's (untranslated) title and
    /// description. Prices always come from the store, already localized.
    /// </summary>
    public static class StoreTexts
    {
        /// <summary>Display name, upper-case in English ("GEM CACHE", "REMOVE ADS").</summary>
        public static string Title(string productId, MonetizationConfig config)
        {
            switch (productId)
            {
                case ProductIds.RemoveAds:
                    return Loc.T("store.remove_ads");
                case ProductIds.GemsSmall:
                    return Loc.T("store.pack_small");
                case ProductIds.GemsMedium:
                    return Loc.T("store.pack_medium");
                case ProductIds.GemsLarge:
                    return Loc.T("store.pack_large");
            }

            IapProductConfig product = config != null ? config.FindProduct(productId) : null;
            return product != null && !string.IsNullOrEmpty(product.Title) ? product.Title : Loc.T("store.pack_fallback");
        }

        /// <summary>One-line description (Remove Ads explains that rewarded ads stay optional).</summary>
        public static string Description(string productId, MonetizationConfig config)
        {
            if (productId == ProductIds.RemoveAds)
            {
                return Loc.T("store.remove_ads_body");
            }

            IapProductConfig product = config != null ? config.FindProduct(productId) : null;
            if (product != null && product.Gems > 0)
            {
                return Loc.T("store.pack_body", ("n", product.Gems));
            }

            return product != null ? product.Description : string.Empty;
        }
    }
}

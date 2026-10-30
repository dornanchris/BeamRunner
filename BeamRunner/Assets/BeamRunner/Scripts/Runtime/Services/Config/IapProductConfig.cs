using System;
using UnityEngine;

namespace BeamRunner.Services
{
    /// <summary>
    /// One in-app product as described in <c>monetization.json</c>. Field names match the JSON keys
    /// (JsonUtility requirement); read them through the PascalCase properties.
    /// </summary>
    [Serializable]
    public sealed class IapProductConfig
    {
        /// <summary>JSON value of <c>type</c> for products that can be bought repeatedly.</summary>
        public const string TypeConsumable = "Consumable";

        /// <summary>JSON value of <c>type</c> for products that are bought once and owned forever.</summary>
        public const string TypeNonConsumable = "NonConsumable";

        [SerializeField] private string id = "";
        [SerializeField] private string type = TypeConsumable;
        [SerializeField] private int gems;
        [SerializeField] private string title = "";
        [SerializeField] private string description = "";
        [SerializeField] private string fallbackPrice = "";

        /// <summary>Creates an empty entry (used by JsonUtility).</summary>
        public IapProductConfig()
        {
        }

        /// <summary>Creates a fully specified entry.</summary>
        public IapProductConfig(string id, bool consumable, int gems, string title, string description,
            string fallbackPrice)
        {
            this.id = id ?? "";
            type = consumable ? TypeConsumable : TypeNonConsumable;
            this.gems = gems;
            this.title = title ?? "";
            this.description = description ?? "";
            this.fallbackPrice = fallbackPrice ?? "";
        }

        /// <summary>Store product id (same on every store).</summary>
        public string Id => id ?? "";

        /// <summary>True for consumables (gem packs); false for non-consumables (Remove Ads).</summary>
        public bool IsConsumable => !string.Equals((type ?? "").Trim(), TypeNonConsumable,
            StringComparison.OrdinalIgnoreCase);

        /// <summary>Gems granted per purchase (0 for non-gem products).</summary>
        public int Gems => Mathf.Max(0, gems);

        /// <summary>Display title for the shop (not localized by the store).</summary>
        public string Title => title ?? "";

        /// <summary>Display description for the shop.</summary>
        public string Description => description ?? "";

        /// <summary>
        /// Price label shown while the store is unavailable (or in the Editor, whose fake store reports $0.01).
        /// May be empty.
        /// </summary>
        public string FallbackPrice => fallbackPrice ?? "";

        internal bool IsValid => !string.IsNullOrEmpty(Id.Trim());

        internal void Sanitize()
        {
            id = (id ?? "").Trim();
            type = IsConsumable ? TypeConsumable : TypeNonConsumable;
            if (gems < 0)
            {
                gems = 0;
            }

            title = title ?? "";
            description = description ?? "";
            fallbackPrice = (fallbackPrice ?? "").Trim();
        }
    }
}

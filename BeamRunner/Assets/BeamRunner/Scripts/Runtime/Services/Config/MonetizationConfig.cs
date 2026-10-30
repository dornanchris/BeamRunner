using System;
using System.Collections.Generic;
using UnityEngine;

namespace BeamRunner.Services
{
    /// <summary>
    /// Ads / IAP / legal-link configuration loaded from <c>Resources/BeamRunner/Config/monetization.json</c>.
    /// Editable without touching code. Missing keys keep their defaults; a missing or corrupt file yields the
    /// full default configuration (ads simulated in the Editor/development builds, IAP catalogue with the
    /// built-in products). Loaded once and cached; use <see cref="Current"/>.
    /// </summary>
    /// <remarks>
    /// Serialized field names are the JSON keys (JsonUtility requirement) and deliberately camelCase;
    /// the public surface is the PascalCase properties.
    /// </remarks>
    [Serializable]
    public sealed class MonetizationConfig
    {
        /// <summary>Resources path of the JSON file (without extension).</summary>
        public const string ResourcePath = "BeamRunner/Config/monetization";

        /// <summary>Default privacy policy URL. A placeholder: the owner must replace it before release.</summary>
        public const string PlaceholderPrivacyUrl = "https://example.com/privacy";

        /// <summary>Default terms of service URL. A placeholder: the owner must replace it before release.</summary>
        public const string PlaceholderTermsUrl = "https://example.com/terms";

        /// <summary>Default support address. A placeholder: the owner must replace it before release.</summary>
        public const string PlaceholderSupportEmail = "support@example.com";

        // Legacy Unity Ads keys (androidGameId, iosGameId, rewardedPlacement*/interstitialPlacement*) are no longer
        // fields, so JsonUtility silently ignores them in old files.
        [SerializeField] private string levelPlayAppKeyAndroid = "";
        [SerializeField] private string levelPlayAppKeyIos = "";
        [SerializeField] private string rewardedAdUnitAndroid = "";
        [SerializeField] private string rewardedAdUnitIos = "";
        [SerializeField] private string interstitialAdUnitAndroid = "";
        [SerializeField] private string interstitialAdUnitIos = "";
        [SerializeField] private bool testMode = true;
        [SerializeField] private bool simulateAdsInEditor = true;
        [SerializeField] private IapProductConfig[] products = CreateDefaultProducts();
        [SerializeField] private string privacyPolicyUrl = PlaceholderPrivacyUrl;
        [SerializeField] private string termsUrl = PlaceholderTermsUrl;
        [SerializeField] private string supportEmail = PlaceholderSupportEmail;
        [SerializeField] private string androidPackage = "";
        [SerializeField] private string iosAppId = "";

        // Crash reporting (Sentry). Empty DSN = off; the Editor only reports when crashReportingInEditor is set.
        [SerializeField] private string crashReportingDsn = "";
        [SerializeField] private bool crashReportingInEditor;

        private static MonetizationConfig s_current;

        [NonSerialized] private bool _loadedFromFile;

        /// <summary>The cached configuration (loaded on first access, never null).</summary>
        public static MonetizationConfig Current
        {
            get
            {
                if (s_current == null)
                {
                    s_current = Load();
                }

                return s_current;
            }
        }

        /// <summary>LevelPlay app key for Android (LevelPlay dashboard → Apps). Empty = not configured.</summary>
        public string LevelPlayAppKeyAndroid => levelPlayAppKeyAndroid ?? "";

        /// <summary>LevelPlay app key for iOS. Empty = not configured.</summary>
        public string LevelPlayAppKeyIos => levelPlayAppKeyIos ?? "";

        /// <summary>LevelPlay rewarded ad unit id on Android (dashboard → Ad units). Empty = format disabled.</summary>
        public string RewardedAdUnitAndroid => rewardedAdUnitAndroid ?? "";

        /// <summary>LevelPlay rewarded ad unit id on iOS. Empty = format disabled.</summary>
        public string RewardedAdUnitIos => rewardedAdUnitIos ?? "";

        /// <summary>LevelPlay interstitial ad unit id on Android. Empty = format disabled.</summary>
        public string InterstitialAdUnitAndroid => interstitialAdUnitAndroid ?? "";

        /// <summary>LevelPlay interstitial ad unit id on iOS. Empty = format disabled.</summary>
        public string InterstitialAdUnitIos => interstitialAdUnitIos ?? "";

        /// <summary>
        /// Development aid, as configured: in <b>development builds</b> the LevelPlay Test Suite is enabled and
        /// opened once after the SDK initializes (see <see cref="EnableAdsTestSuite"/>). Release builds ignore it;
        /// test ads themselves are configured per device in the LevelPlay dashboard, not in code.
        /// </summary>
        public bool TestMode => testMode;

        /// <summary>
        /// When LevelPlay is not configured for the platform (and always in the Editor), show a simulated
        /// full-screen ad in the Editor and in development builds. Release builds without LevelPlay ids never
        /// simulate; ads are then simply unavailable.
        /// </summary>
        public bool SimulateAdsInEditor => simulateAdsInEditor;

        /// <summary>The IAP catalogue (never null; entries have non-empty ids).</summary>
        public IReadOnlyList<IapProductConfig> Products => products ?? Array.Empty<IapProductConfig>();

        /// <summary>Privacy policy URL (placeholder until the owner sets it).</summary>
        public string PrivacyPolicyUrl => privacyPolicyUrl ?? "";

        /// <summary>Terms of service URL (placeholder until the owner sets it).</summary>
        public string TermsUrl => termsUrl ?? "";

        /// <summary>Support e-mail address (placeholder until the owner sets it).</summary>
        public string SupportEmail => supportEmail ?? "";

        /// <summary>Google Play package name used for store/rating links; falls back to <see cref="Application.identifier"/>.</summary>
        public string AndroidPackage
        {
            get
            {
                if (!string.IsNullOrEmpty(androidPackage))
                {
                    return androidPackage;
                }

                try
                {
                    return Application.identifier ?? "";
                }
                catch (Exception)
                {
                    return "";
                }
            }
        }

        /// <summary>Numeric Apple App Store id (App Store Connect → App Information → Apple ID). Empty = unknown.</summary>
        public string IosAppId => iosAppId ?? "";

        /// <summary>
        /// Sentry DSN for crash reports (Sentry ▸ Project Settings ▸ Client Keys). Empty = crash reporting off. The same
        /// DSN serves Android and iOS (events carry the platform).
        /// </summary>
        public string CrashReportingDsn => crashReportingDsn ?? "";

        /// <summary>Debug flag: also report from the Unity Editor (off by default; players are never affected).</summary>
        public bool CrashReportingInEditor => crashReportingInEditor;

        /// <summary>True when the values came from the JSON file rather than built-in defaults.</summary>
        public bool LoadedFromFile => _loadedFromFile;

        /// <summary>True while any legal/support link is still a placeholder value.</summary>
        public bool HasPlaceholderLinks =>
            IsPlaceholder(PrivacyPolicyUrl) || IsPlaceholder(TermsUrl) || IsPlaceholder(SupportEmail);

        /// <summary>LevelPlay app key for the current build platform (empty on platforms without ads).</summary>
        public string LevelPlayAppKey
        {
            get
            {
#if UNITY_IOS
                return LevelPlayAppKeyIos;
#elif UNITY_ANDROID
                return LevelPlayAppKeyAndroid;
#else
                return "";
#endif
            }
        }

        /// <summary>Rewarded ad unit id for the current build platform (empty = disabled / no ads).</summary>
        public string RewardedAdUnit
        {
            get
            {
#if UNITY_IOS
                return RewardedAdUnitIos;
#elif UNITY_ANDROID
                return RewardedAdUnitAndroid;
#else
                return "";
#endif
            }
        }

        /// <summary>Interstitial ad unit id for the current build platform (empty = disabled / no ads).</summary>
        public string InterstitialAdUnit
        {
            get
            {
#if UNITY_IOS
                return InterstitialAdUnitIos;
#elif UNITY_ANDROID
                return InterstitialAdUnitAndroid;
#else
                return "";
#endif
            }
        }

        /// <summary>
        /// True when LevelPlay can serve something on the current build platform: an app key plus at least one
        /// ad unit id.
        /// </summary>
        public bool HasLevelPlayConfig =>
            LevelPlayAppKey.Length > 0 && (RewardedAdUnit.Length > 0 || InterstitialAdUnit.Length > 0);

        /// <summary>
        /// Whether the LevelPlay Test Suite is enabled: <see cref="TestMode"/> in a development build only.
        /// </summary>
        public bool EnableAdsTestSuite
        {
            get
            {
                try
                {
                    return testMode && Debug.isDebugBuild;
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        /// <summary>Returns the catalogue entry for <paramref name="productId"/>, or null.</summary>
        public IapProductConfig FindProduct(string productId)
        {
            if (string.IsNullOrEmpty(productId) || products == null)
            {
                return null;
            }

            for (int i = 0; i < products.Length; i++)
            {
                IapProductConfig product = products[i];
                if (product != null && string.Equals(product.Id, productId, StringComparison.Ordinal))
                {
                    return product;
                }
            }

            return null;
        }

        /// <summary>
        /// Loads the configuration from Resources (bypassing the cache). Never throws and never returns null.
        /// </summary>
        public static MonetizationConfig Load()
        {
            var config = new MonetizationConfig();
            try
            {
                var asset = Resources.Load<TextAsset>(ResourcePath);
                if (asset == null)
                {
                    ServiceLog.Warn("monetization.json not found in Resources; using built-in defaults.");
                }
                else
                {
                    string json = asset.text;
                    if (string.IsNullOrWhiteSpace(json))
                    {
                        ServiceLog.Warn("monetization.json is empty; using built-in defaults.");
                    }
                    else
                    {
                        JsonUtility.FromJsonOverwrite(json, config);
                        config._loadedFromFile = true;
                    }

                    Resources.UnloadAsset(asset);
                }
            }
            catch (Exception e)
            {
                ServiceLog.Warn("monetization.json could not be parsed (" + e.Message + "); using built-in defaults.");
                config = new MonetizationConfig();
            }

            config.Sanitize();
            if (config.HasPlaceholderLinks)
            {
                ServiceLog.Warn("monetization.json still contains placeholder privacy/terms/support values. " +
                                "Replace them before release.");
            }

            return config;
        }

        /// <summary>Drops the cached instance so the next <see cref="Current"/> re-reads the file.</summary>
        public static void ClearCache()
        {
            s_current = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_current = null;
        }

        private void Sanitize()
        {
            levelPlayAppKeyAndroid = (levelPlayAppKeyAndroid ?? "").Trim();
            levelPlayAppKeyIos = (levelPlayAppKeyIos ?? "").Trim();
            rewardedAdUnitAndroid = (rewardedAdUnitAndroid ?? "").Trim();
            rewardedAdUnitIos = (rewardedAdUnitIos ?? "").Trim();
            interstitialAdUnitAndroid = (interstitialAdUnitAndroid ?? "").Trim();
            interstitialAdUnitIos = (interstitialAdUnitIos ?? "").Trim();
            privacyPolicyUrl = (privacyPolicyUrl ?? "").Trim();
            termsUrl = (termsUrl ?? "").Trim();
            supportEmail = (supportEmail ?? "").Trim();
            androidPackage = (androidPackage ?? "").Trim();
            iosAppId = (iosAppId ?? "").Trim();
            crashReportingDsn = (crashReportingDsn ?? "").Trim();

            if (products == null)
            {
                products = CreateDefaultProducts();
            }

            var clean = new List<IapProductConfig>(products.Length);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < products.Length; i++)
            {
                IapProductConfig product = products[i];
                if (product == null)
                {
                    continue;
                }

                product.Sanitize();
                if (!product.IsValid)
                {
                    ServiceLog.Warn("monetization.json: skipping a product without an id.");
                    continue;
                }

                if (!seen.Add(product.Id))
                {
                    ServiceLog.Warn("monetization.json: duplicate product id '" + product.Id + "' ignored.");
                    continue;
                }

                clean.Add(product);
            }

            products = clean.ToArray();
        }

        private static bool IsPlaceholder(string value)
        {
            return string.IsNullOrEmpty(value) ||
                   value.IndexOf("example.com", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static IapProductConfig[] CreateDefaultProducts()
        {
            return new[]
            {
                new IapProductConfig(ProductIds.RemoveAds, false, 0, "Remove Ads",
                    "No more interstitial ads between runs. Rewarded ads stay optional.", "$2.99"),
                new IapProductConfig(ProductIds.GemsSmall, true, 1000, "Gem Cache",
                    "1,000 gems.", "$0.99"),
                new IapProductConfig(ProductIds.GemsMedium, true, 6000, "Gem Vault",
                    "6,000 gems.", "$4.99"),
                new IapProductConfig(ProductIds.GemsLarge, true, 15000, "Gem Hoard",
                    "15,000 gems.", "$9.99"),
            };
        }
    }
}

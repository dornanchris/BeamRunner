#if UNITY_EDITOR || DEVELOPMENT_BUILD
using BeamRunner.Core.Diagnostics;
#endif
using UnityEngine;

namespace BeamRunner.Services
{
    /// <summary>Chooses the IAP implementation for the current platform.</summary>
    public static class IapServiceFactory
    {
        /// <summary>
        /// Android (Google Play) / iOS (App Store) players and the Editor (Unity IAP 5's fake store, which asks
        /// Buy/Cancel in a dialog) → <see cref="UnityIapService"/>; desktop, WebGL or an empty catalogue →
        /// <see cref="NullIapService"/>. Desktop players are excluded on purpose: there Unity IAP would use its
        /// fake store, which hands out free purchases (macOS: the Mac App Store, where the game is not listed).
        /// </summary>
        public static IIapService Create(MonetizationConfig config)
        {
            if (config == null)
            {
                config = MonetizationConfig.Current;
            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (TestLaunchOptions.OfflineServices)
            {
                // Automated tests: no store connection, no purchase dialogs, no IAP grant ledger in PlayerPrefs.
                ServiceLog.Info("IAP: store disabled (test launch options).");
                return new NullIapService(config);
            }
#endif

            if (config.Products.Count == 0)
            {
                ServiceLog.Info("IAP: no products configured; store disabled.");
                return new NullIapService(config);
            }

            RuntimePlatform platform = Application.platform;
            if (Application.isEditor || platform == RuntimePlatform.Android || platform == RuntimePlatform.IPhonePlayer)
            {
                return new UnityIapService(config);
            }

            ServiceLog.Info("IAP: no supported store on " + platform + "; store disabled.");
            return new NullIapService(config);
        }
    }
}

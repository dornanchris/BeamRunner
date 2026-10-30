using System;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using BeamRunner.Core.Diagnostics;
#endif
using UnityEngine;

namespace BeamRunner.Services
{
    /// <summary>Chooses the ads implementation for the current platform and configuration.</summary>
    public static class AdsServiceFactory
    {
        /// <summary>
        /// <list type="bullet">
        /// <item>Android/iOS device build with a LevelPlay app key and at least one ad unit id for that platform
        /// (<see cref="MonetizationConfig.HasLevelPlayConfig"/>) → <see cref="LevelPlayAdsService"/>.</item>
        /// <item>Otherwise, in the Editor (always — LevelPlay's Editor mock ads do not reliably raise their init
        /// and reward callbacks) or a development build, with <c>simulateAdsInEditor</c> →
        /// <see cref="SimulatedAdsService"/>.</item>
        /// <item>Otherwise (release build without LevelPlay ids, desktop, WebGL) → <see cref="NullAdsService"/>.</item>
        /// </list>
        /// Never throws.
        /// </summary>
        public static IAdsService Create(MonetizationConfig config)
        {
            if (config == null)
            {
                config = MonetizationConfig.Current;
            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (TestLaunchOptions.OfflineServices)
            {
                // Automated tests: always the local simulation (consent dialog + rewarded/interstitial flows, no network).
                ServiceLog.Info("Ads: SIMULATED (test launch options).");
                return new SimulatedAdsService();
            }
#endif

#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
            if (config.HasLevelPlayConfig)
            {
                bool supported;
                try
                {
                    supported = Application.isMobilePlatform;
                }
                catch (Exception e)
                {
                    ServiceLog.Exception("Ads: Application.isMobilePlatform threw", e);
                    supported = false;
                }

                if (supported)
                {
                    return new LevelPlayAdsService(config.LevelPlayAppKey, config.RewardedAdUnit,
                        config.InterstitialAdUnit, config.EnableAdsTestSuite);
                }

                ServiceLog.Warn("Ads: LevelPlay is not supported on this device.");
            }
#endif

            bool devEnvironment;
            try
            {
                devEnvironment = Application.isEditor || Debug.isDebugBuild;
            }
            catch (Exception)
            {
                devEnvironment = false;
            }

            if (config.SimulateAdsInEditor && devEnvironment)
            {
                return new SimulatedAdsService();
            }

            ServiceLog.Info("Ads: no ad network configured for this platform; ads disabled.");
            return new NullAdsService();
        }
    }
}

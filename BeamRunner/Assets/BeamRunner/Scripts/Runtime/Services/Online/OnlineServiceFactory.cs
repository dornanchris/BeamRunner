using System;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using BeamRunner.Core.Diagnostics;
#endif

namespace BeamRunner.Services
{
    /// <summary>Chooses the online services implementation for the platform and <see cref="OnlineConfig"/>.</summary>
    public static class OnlineServiceFactory
    {
        /// <summary>
        /// <list type="bullet">
        /// <item>Android device with something configured for Google Play Games (a leaderboard / achievement id or
        /// <c>cloudSaveAndroid</c>) AND the plugin's Android setup done → <c>PlayGamesOnlineService</c>.</item>
        /// <item>iOS device with something configured for Game Center / iCloud → <c>GameCenterOnlineService</c>.</item>
        /// <item>Otherwise, in the Editor or a development build with <c>simulateInEditor</c> →
        /// <see cref="SimulatedOnlineService"/>.</item>
        /// <item>Otherwise (the default: nothing configured) and in automated tests → <see cref="NullOnlineService"/>:
        /// every online UI stays hidden and nothing is initialised.</item>
        /// </list>
        /// Never throws.
        /// </summary>
        public static IOnlineService Create(OnlineConfig config)
        {
            if (config == null)
            {
                config = OnlineConfig.Current;
            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (TestLaunchOptions.OfflineServices)
            {
                ServiceLog.Info("Online: off (test launch options).");
                return new NullOnlineService();
            }
#endif

            try
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                if (config.HasAnyFeature(OnlinePlatform.GooglePlayGames))
                {
                    if (PlayGamesOnlineService.IsConfigured)
                    {
                        return new PlayGamesOnlineService(config);
                    }

                    ServiceLog.Warn("Online: online.json enables Google Play Games features but the plugin's Android " +
                                    "setup (Window > Google Play Games > Setup) was not run; Play Games stays off.");
                }
#elif UNITY_IOS && !UNITY_EDITOR
                if (config.HasAnyFeature(OnlinePlatform.GameCenter))
                {
                    return new GameCenterOnlineService(config);
                }
#endif

#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if (config.SimulateInEditor)
                {
                    ServiceLog.Info("Online: SIMULATED (simulateInEditor in online.json).");
                    return new SimulatedOnlineService(config);
                }
#endif
            }
            catch (Exception e)
            {
                ServiceLog.Exception("Online: creating the service failed; online features off", e);
            }

            return new NullOnlineService();
        }
    }
}

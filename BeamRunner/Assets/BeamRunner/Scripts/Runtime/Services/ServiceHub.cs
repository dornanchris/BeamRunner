using System;
using UnityEngine;

namespace BeamRunner.Services
{
    /// <summary>
    /// Creates and holds the platform services. Two phases, both idempotent and main-thread only:
    /// <list type="number">
    /// <item><see cref="Create"/> (or first access to any property) builds <see cref="Save"/>, <see cref="Ads"/>
    /// and <see cref="Iap"/> without touching the network, so the profile can be loaded and event handlers
    /// subscribed first.</item>
    /// <item><see cref="Initialize"/> starts the store connection and — after the iOS App Tracking Transparency
    /// answer (<see cref="TrackingConsent"/>) — the ad SDK with the player's consent.</item>
    /// </list>
    /// Typical boot: <c>ServiceHub.Create(this)</c> → load profile from <c>ServiceHub.Save</c> → subscribe
    /// <c>Iap.EntitlementGranted</c> (grant + save) and <c>Ads.AdShowingChanged</c> (mute/pause) → consent dialog on
    /// first launch → <c>ServiceHub.Initialize(consent, profile.AdsRemoved)</c>.
    /// </summary>
    public static class ServiceHub
    {
        private static bool s_created;
        private static bool s_initialized;
        private static bool s_playerAdsConsent;
        private static bool s_trackingHooked;
        private static ISaveStorage s_save;
        private static IAdsService s_ads;
        private static IIapService s_iap;
        private static IOnlineService s_online;

        /// <summary>True once the service instances exist.</summary>
        public static bool IsCreated => s_created;

        /// <summary>True once <see cref="Initialize"/> has run.</summary>
        public static bool IsInitialized => s_initialized;

        /// <summary>The monetization configuration (loaded on first use).</summary>
        public static MonetizationConfig Config => MonetizationConfig.Current;

        /// <summary>Profile storage (file-based; PlayerPrefs on WebGL).</summary>
        public static ISaveStorage Save
        {
            get
            {
                EnsureCreated();
                return s_save;
            }
        }

        /// <summary>Ads (LevelPlay, Editor/dev simulation, or a null service).</summary>
        public static IAdsService Ads
        {
            get
            {
                EnsureCreated();
                return s_ads;
            }
        }

        /// <summary>In-app purchases (Unity IAP or a null service).</summary>
        public static IIapService Iap
        {
            get
            {
                EnsureCreated();
                return s_iap;
            }
        }

        /// <summary>
        /// Online services: Google Play Games / Game Center sign-in, leaderboards, achievements and cloud save (or a
        /// null service when <c>online.json</c> configures nothing). Created with the others, started by the game once
        /// the first-launch consent flow is done (<see cref="IOnlineService.Start"/>).
        /// </summary>
        public static IOnlineService Online
        {
            get
            {
                EnsureCreated();
                return s_online;
            }
        }

        /// <summary>
        /// The personalised-ads flag actually applied to the ad network: the player's GDPR/CCPA choice AND, on
        /// iOS, an "authorized" App Tracking Transparency status.
        /// </summary>
        public static bool EffectiveAdsConsent => s_playerAdsConsent && TrackingConsent.AllowsPersonalizedAds;

        /// <summary>
        /// Builds the services (no network activity). <paramref name="host"/> (optional, e.g. the bootstrap
        /// component) hosts the hidden service pump and must live for the whole session; without it a hidden
        /// <c>DontDestroyOnLoad</c> object is used. <paramref name="saveValidator"/> (optional) returns true for
        /// save documents that deserialize into a usable profile, enabling fallback to the backup copy.
        /// </summary>
        public static void Create(MonoBehaviour host = null, Func<string, bool> saveValidator = null)
        {
            if (s_created)
            {
                return;
            }

            s_created = true;
            ServiceRunner.Ensure(host);
            MonetizationConfig config = MonetizationConfig.Current;

            try
            {
                s_save = SaveStorageFactory.Create(saveValidator);
            }
            catch (Exception e)
            {
                ServiceLog.Exception("Creating save storage failed; using PlayerPrefs", e);
                s_save = new PlayerPrefsSaveStorage(PlayerPrefsSaveStorage.DefaultKey, saveValidator,
                    BeamRunner.Core.Meta.SaveIntegrityKey.Get());
            }

            try
            {
                s_ads = AdsServiceFactory.Create(config);
            }
            catch (Exception e)
            {
                ServiceLog.Exception("Creating the ads service failed; ads disabled", e);
                s_ads = new NullAdsService();
            }

            try
            {
                s_iap = IapServiceFactory.Create(config);
            }
            catch (Exception e)
            {
                ServiceLog.Exception("Creating the IAP service failed; store disabled", e);
                s_iap = new NullIapService(config);
            }

            // Keep interstitials off whenever the store reports Remove Ads as owned, even before the game has
            // processed the entitlement. (Deliberately not an EntitlementGranted subscriber, so transactions are
            // still held until the game's granting handler subscribes.)
            s_iap.AvailabilityChanged += SyncAdsRemovedFromStore;

            try
            {
                s_online = OnlineServiceFactory.Create(OnlineConfig.Current);
            }
            catch (Exception e)
            {
                ServiceLog.Exception("Creating the online services failed; online features off", e);
                s_online = new NullOnlineService();
            }

            ServiceLog.Info("Services created: save=" + s_save.GetType().Name + ", ads=" + s_ads.GetType().Name +
                            ", iap=" + s_iap.GetType().Name + ", online=" + s_online.GetType().Name + ".");
        }

        /// <summary>
        /// Starts ads and IAP. <paramref name="personalizedAdsConsent"/>: the player's GDPR/CCPA choice (pass
        /// false until they have answered). <paramref name="adsRemoved"/>: "Remove Ads" already owned per the
        /// profile. Later calls only update consent (and can switch interstitials off).
        /// </summary>
        /// <remarks>
        /// On an iOS device with a real ad network, when the player allowed personalised ads and has not answered
        /// the App Tracking Transparency prompt yet, the prompt is shown first (once) and the ad SDK starts after
        /// the answer; without an "authorized" status the ads are non-personalised whatever the player chose.
        /// Players who chose non-personalised ads are not asked (nothing to track); if they switch to personalised
        /// later, <see cref="SetAdsConsent"/> asks then. IAP starts immediately.
        /// </remarks>
        public static void Initialize(bool personalizedAdsConsent, bool adsRemoved)
        {
            EnsureCreated();
            s_playerAdsConsent = personalizedAdsConsent;
            if (s_initialized)
            {
                ApplyAdsConsentAfterTracking();
                if (adsRemoved)
                {
                    SafeRun(() => s_ads.SetAdsRemoved(true), "Disabling interstitials");
                }

                return;
            }

            s_initialized = true;
            HookTracking();
            SafeRun(() => s_ads.SetAdsRemoved(adsRemoved), "Applying the Remove Ads flag");
            SafeRun(() => s_iap.Initialize(), "Initializing IAP");
            AfterTrackingConsent(() => SafeRun(() => s_ads.Initialize(EffectiveAdsConsent), "Initializing ads"));
        }

        /// <summary>
        /// Updates the personalized-ads consent (e.g. from the settings screen). After <see cref="Initialize"/>,
        /// switching to personalised on iOS first asks for tracking authorization if it was never asked.
        /// </summary>
        public static void SetAdsConsent(bool personalizedAdsConsent)
        {
            EnsureCreated();
            s_playerAdsConsent = personalizedAdsConsent;
            if (!s_initialized)
            {
                SafeRun(() => s_ads.SetConsent(EffectiveAdsConsent), "Updating ads consent");
                return;
            }

            ApplyAdsConsentAfterTracking();
        }

        private static void ApplyAdsConsentAfterTracking()
        {
            AfterTrackingConsent(() => SafeRun(() => s_ads.SetConsent(EffectiveAdsConsent), "Updating ads consent"));
        }

        /// <summary>
        /// Runs <paramref name="action"/> once the ATT question is settled: now when there is nothing to ask,
        /// otherwise after the prompt (callbacks run in request order and read the latest consent then).
        /// </summary>
        private static void AfterTrackingConsent(Action action)
        {
            bool wait;
            try
            {
                bool ask = s_playerAdsConsent && UsesRealAdNetwork() && TrackingConsent.CanPrompt;
                wait = ask || TrackingConsent.IsRequestPending;
                if (wait)
                {
                    TrackingConsent.Request(_ => action());
                }
            }
            catch (Exception e)
            {
                ServiceLog.Exception("Requesting tracking authorization failed", e);
                wait = false;
            }

            if (!wait)
            {
                action();
            }
        }

        private static bool UsesRealAdNetwork()
        {
            return s_ads != null && !(s_ads is NullAdsService) && !s_ads.IsSimulated;
        }

        private static void HookTracking()
        {
            if (s_trackingHooked)
            {
                return;
            }

            s_trackingHooked = true;
            TrackingConsent.StatusChanged += OnTrackingStatusChanged;
        }

        private static void OnTrackingStatusChanged()
        {
            if (s_initialized && s_ads != null)
            {
                SafeRun(() => s_ads.SetConsent(EffectiveAdsConsent), "Re-applying ads consent after an ATT change");
            }
        }

        private static void EnsureCreated()
        {
            if (!s_created)
            {
                Create();
            }
        }

        private static void SyncAdsRemovedFromStore()
        {
            try
            {
                if (s_iap != null && s_ads != null && s_iap.Owns(ProductIds.RemoveAds))
                {
                    s_ads.SetAdsRemoved(true);
                }
            }
            catch (Exception e)
            {
                ServiceLog.Exception("Syncing Remove Ads failed", e);
            }
        }

        private static void SafeRun(Action action, string what)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                ServiceLog.Exception(what + " failed", e);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_created = false;
            s_initialized = false;
            s_playerAdsConsent = false;
            s_trackingHooked = false;
            s_save = null;
            s_ads = null;
            s_iap = null;
            s_online = null;
        }
    }
}

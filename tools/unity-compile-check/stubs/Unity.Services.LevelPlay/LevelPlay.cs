// STUB of com.unity.services.levelplay 9.5.1 (Unity LevelPlay / "Ads Mediation"), for compile checks only.
//
// Assembly name "Unity.LevelPlay" (the real runtime asmdef: wagenheimer/UnityLevelPlayHelper's runtime asmdef references
// "Unity.LevelPlay" and its editor checks resolve Type.GetType("Unity.Services.LevelPlay.LevelPlay, Unity.LevelPlay")),
// namespace Unity.Services.LevelPlay. The package source is not publicly mirrored, so this is a hand-written public API
// SUBSET. Bodies do nothing. Keep it small: if game code needs a member that is missing here, add it with the REAL 9.5.1
// signature (https://docs.unity.com/grow/levelplay/sdk/unity/) and mark where it came from.
//
// Provenance of every member:
//   [usage]  verified by real usage in wagenheimer/UnityLevelPlayHelper 2.12.0 (Runtime/LevelPlayHelper.cs, which
//            depends on com.unity.services.levelplay 9.5.1): member exists with exactly this shape (static vs
//            instance, parameter/return types, event delegate types - all handlers there are method groups whose
//            signatures pin the delegate parameter types; nullable-ness of Revenue pinned by `?? 0d`).
//   [docs]   from the LevelPlay 9.x Unity docs / 8.x->9.0 migration guide only (not verified by compiled usage).
//
// Deliberately NOT stubbed (removed in LevelPlay 9.x, per the helper's "deprecated / removed LevelPlay APIs" check and
// the 9.0 migration guide): IronSource.Agent / IronSourceEvents, the com.unity3d.mediation namespace,
// LevelPlay.SetConsent, "do_not_sell" / "is_child_directed" metadata (use LevelPlayPrivacySettings),
// LevelPlayAdDisplayInfoError (9.x OnAdDisplayFailed passes (LevelPlayAdInfo, LevelPlayAdError)), the adFormats
// parameter of LevelPlay.Init. Also not stubbed (exist, but unverified shapes): LevelPlayAdSize / Config.SetSize,
// LevelPlayPrivacySettings.SetGDPRConsents, LevelPlay.SetDynamicUserId / SetSegment, banner Pause/ResumeAutoRefresh,
// custom (Vector2) banner positions. Constructors of the data classes are internal on purpose (unverified).
using System;

#pragma warning disable CS0067   // stub events are never raised

namespace Unity.Services.LevelPlay
{
    /// <summary>SDK entry point.</summary>
    public static class LevelPlay
    {
        /// [usage] OnInitSuccess += (LevelPlayConfiguration) handler.
        public static event Action<LevelPlayConfiguration> OnInitSuccess;
        /// [usage] OnInitFailed += (LevelPlayInitError) handler.
        public static event Action<LevelPlayInitError> OnInitFailed;
        /// [usage: existence + deprecation] global impression-data event; the helper reports it as deprecated on 9.5.0+
        /// in favour of the per-ad-instance OnAdImpressionDataReady events.
        [Obsolete("Deprecated in LevelPlay 9.5.0: use the OnAdImpressionDataReady event of the ad instance (LevelPlayRewardedAd / LevelPlayInterstitialAd / LevelPlayBannerAd).")]
        public static event Action<LevelPlayImpressionData> OnImpressionDataReady;

        /// [usage] Init(appKey); [docs] optional userId.
        public static void Init(string appKey, string userId = null) { }
        /// [usage] SetMetaData("is_test_suite", "enable").
        public static void SetMetaData(string key, string value) { }
        /// [usage]
        public static void LaunchTestSuite() { }
        /// [docs] logs the integration status of the SDK / adapters.
        public static void ValidateIntegration() { }
        /// [docs] iOS: pause the game (Time.timeScale / audio) while a full-screen ad is shown.
        public static void SetPauseGame(bool pause) { }
    }

    /// [usage] privacy / regulation flags, applied before LevelPlay.Init (9.4.0+).
    public static class LevelPlayPrivacySettings
    {
        public static void SetGDPRConsent(bool hasConsent) { }
        public static void SetCCPA(bool doNotSell) { }
        public static void SetCOPPA(bool isChildDirected) { }
    }

    /// [usage] argument of LevelPlay.OnInitSuccess.
    public class LevelPlayConfiguration
    {
        internal LevelPlayConfiguration() { }
        /// [docs]
        public bool IsAdQualityEnabled { get; }
    }

    /// [usage] argument of LevelPlay.OnInitFailed: ErrorCode (formatted), ErrorMessage (string).
    public class LevelPlayInitError
    {
        internal LevelPlayInitError() { }
        public int ErrorCode { get; }
        public string ErrorMessage { get; }
    }

    /// [usage] error of OnAdLoadFailed / OnAdDisplayFailed: ErrorCode (value type, .ToString()), ErrorMessage (string).
    public class LevelPlayAdError
    {
        internal LevelPlayAdError() { }
        public int ErrorCode { get; }
        public string ErrorMessage { get; }
        /// [docs]
        public string AdUnitId { get; }
    }

    /// [usage] AdNetwork, PlacementName (string), Revenue (double?), ToString(); the rest [docs].
    public class LevelPlayAdInfo
    {
        internal LevelPlayAdInfo() { }
        public string AdNetwork { get; }
        public string PlacementName { get; }
        public double? Revenue { get; }
        public string AdId { get; }
        public string AdUnitId { get; }
        public string AdUnitName { get; }
        public string AdFormat { get; }
        public string AuctionId { get; }
        public string Country { get; }
        public string InstanceName { get; }
        public string InstanceId { get; }
        public string Precision { get; }
        public override string ToString() => base.ToString();
    }

    /// [usage] second argument of LevelPlayRewardedAd.OnAdRewarded: Name, Amount.
    public class LevelPlayReward
    {
        internal LevelPlayReward() { }
        public string Name { get; }
        public int Amount { get; }
    }

    /// [usage] per-impression revenue data (OnAdImpressionDataReady): Revenue (double?, `?? 0d`), AdNetwork, AdFormat,
    /// MediationAdUnitId (string); the rest [docs].
    public class LevelPlayImpressionData
    {
        internal LevelPlayImpressionData() { }
        public double? Revenue { get; }
        public string AdNetwork { get; }
        public string AdFormat { get; }
        public string MediationAdUnitId { get; }
        public string MediationAdUnitName { get; }
        public string AuctionId { get; }
        public string Country { get; }
        public string Placement { get; }
        public string InstanceName { get; }
        public string InstanceId { get; }
        public string Precision { get; }
        public string EncryptedCPM { get; }
        public string CreativeId { get; }
    }

    /// [docs] ad formats.
    public enum LevelPlayAdFormat
    {
        REWARDED,
        INTERSTITIAL,
        BANNER,
        NATIVE_AD,
    }

    /// Rewarded ad unit. [usage]: ctor(adUnitId), LoadAd(), ShowAd(), IsAdReady(), all events except as noted.
    public sealed class LevelPlayRewardedAd
    {
        /// [docs] optional per-instance configuration: new LevelPlayRewardedAd.Config.Builder().SetBidFloor(x).Build().
        public sealed class Config
        {
            Config() { }

            public sealed class Builder
            {
                public Builder SetBidFloor(double bidFloor) => this;
                public Config Build() => new Config();
            }
        }

        public LevelPlayRewardedAd(string adUnitId) { }
        /// [docs]
        public LevelPlayRewardedAd(string adUnitId, Config config) { }

        public event Action<LevelPlayAdInfo> OnAdLoaded;
        public event Action<LevelPlayAdError> OnAdLoadFailed;
        public event Action<LevelPlayAdInfo> OnAdDisplayed;
        public event Action<LevelPlayAdInfo, LevelPlayAdError> OnAdDisplayFailed;
        public event Action<LevelPlayAdInfo, LevelPlayReward> OnAdRewarded;
        public event Action<LevelPlayAdInfo> OnAdClicked;
        public event Action<LevelPlayAdInfo> OnAdClosed;
        public event Action<LevelPlayAdInfo> OnAdInfoChanged;
        /// [usage] per-instance impression-level revenue (9.5.0+).
        public event Action<LevelPlayImpressionData> OnAdImpressionDataReady;

        public void LoadAd() { }
        /// [usage] ShowAd(); [docs] optional placement name.
        public void ShowAd(string placementName = null) { }
        public bool IsAdReady() => false;
        /// [docs] (the helper calls DestroyAd on interstitials; rewarded has the same API)
        public void DestroyAd() { }
        /// [docs]
        public static bool IsPlacementCapped(string placementName) => false;
    }

    /// Interstitial ad unit. [usage]: ctor(adUnitId), LoadAd(), ShowAd(), IsAdReady(), DestroyAd(), all events.
    public sealed class LevelPlayInterstitialAd
    {
        /// [docs] optional per-instance configuration: new LevelPlayInterstitialAd.Config.Builder().SetBidFloor(x).Build().
        public sealed class Config
        {
            Config() { }

            public sealed class Builder
            {
                public Builder SetBidFloor(double bidFloor) => this;
                public Config Build() => new Config();
            }
        }

        public LevelPlayInterstitialAd(string adUnitId) { }
        /// [docs]
        public LevelPlayInterstitialAd(string adUnitId, Config config) { }

        public event Action<LevelPlayAdInfo> OnAdLoaded;
        public event Action<LevelPlayAdError> OnAdLoadFailed;
        public event Action<LevelPlayAdInfo> OnAdDisplayed;
        public event Action<LevelPlayAdInfo, LevelPlayAdError> OnAdDisplayFailed;
        public event Action<LevelPlayAdInfo> OnAdClicked;
        public event Action<LevelPlayAdInfo> OnAdClosed;
        public event Action<LevelPlayAdInfo> OnAdInfoChanged;
        /// [usage] per-instance impression-level revenue (9.5.0+).
        public event Action<LevelPlayImpressionData> OnAdImpressionDataReady;

        public void LoadAd() { }
        /// [usage] ShowAd(); [docs] optional placement name.
        public void ShowAd(string placementName = null) { }
        public bool IsAdReady() => false;
        public void DestroyAd() { }
        /// [docs]
        public static bool IsPlacementCapped(string placementName) => false;
    }

    /// Banner ad unit. [usage]: ctor(adUnitId, Config), Config.Builder (SetPosition/SetDisplayOnLoad/SetRespectSafeArea/
    /// Build), LoadAd/ShowAd/HideAd/DestroyAd, all events.
    public sealed class LevelPlayBannerAd
    {
        public sealed class Config
        {
            Config() { }

            public sealed class Builder
            {
                public Builder SetPosition(LevelPlayBannerPosition position) => this;
                public Builder SetDisplayOnLoad(bool displayOnLoad) => this;
                public Builder SetRespectSafeArea(bool respectSafeArea) => this;
                /// [docs]
                public Builder SetPlacementName(string placementName) => this;
                /// [docs]
                public Builder SetBidFloor(double bidFloor) => this;
                public Config Build() => new Config();
            }
        }

        public LevelPlayBannerAd(string adUnitId, Config config) { }
        /// [docs]
        public LevelPlayBannerAd(string adUnitId) { }

        public event Action<LevelPlayAdInfo> OnAdLoaded;
        public event Action<LevelPlayAdError> OnAdLoadFailed;
        public event Action<LevelPlayAdInfo> OnAdDisplayed;
        public event Action<LevelPlayAdInfo, LevelPlayAdError> OnAdDisplayFailed;
        public event Action<LevelPlayAdInfo> OnAdClicked;
        public event Action<LevelPlayAdInfo> OnAdExpanded;
        public event Action<LevelPlayAdInfo> OnAdCollapsed;
        public event Action<LevelPlayAdInfo> OnAdLeftApplication;
        public event Action<LevelPlayImpressionData> OnAdImpressionDataReady;

        public void LoadAd() { }
        public void ShowAd() { }
        public void HideAd() { }
        public void DestroyAd() { }
    }

    /// [usage] the nine preset banner positions (used as values; a class with static members in the real SDK - do
    /// not `switch` on it).
    public sealed class LevelPlayBannerPosition
    {
        LevelPlayBannerPosition() { }
        public static readonly LevelPlayBannerPosition TopLeft = new LevelPlayBannerPosition();
        public static readonly LevelPlayBannerPosition TopCenter = new LevelPlayBannerPosition();
        public static readonly LevelPlayBannerPosition TopRight = new LevelPlayBannerPosition();
        public static readonly LevelPlayBannerPosition CenterLeft = new LevelPlayBannerPosition();
        public static readonly LevelPlayBannerPosition Center = new LevelPlayBannerPosition();
        public static readonly LevelPlayBannerPosition CenterRight = new LevelPlayBannerPosition();
        public static readonly LevelPlayBannerPosition BottomLeft = new LevelPlayBannerPosition();
        public static readonly LevelPlayBannerPosition BottomCenter = new LevelPlayBannerPosition();
        public static readonly LevelPlayBannerPosition BottomRight = new LevelPlayBannerPosition();
    }
}

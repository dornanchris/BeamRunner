using System;

namespace BeamRunner.Services
{
    /// <summary>
    /// Rewarded + interstitial ads. All members are main-thread only. Callbacks and events are always raised on
    /// the main thread, never synchronously from inside <see cref="ShowRewarded"/>/<see cref="ShowInterstitial"/>,
    /// and every show request is guaranteed to call back exactly once (time-outs included), so the game never
    /// hangs waiting for an ad. Implementations never throw into gameplay.
    /// </summary>
    public interface IAdsService
    {
        /// <summary>
        /// Raised with <c>true</c> right before a full-screen ad appears and with <c>false</c> right after it is
        /// gone (before the show callback runs). Mute audio / suppress auto-pause while it is <c>true</c>.
        /// </summary>
        event Action<bool> AdShowingChanged;

        /// <summary>Raised whenever <see cref="RewardedReady"/> or <see cref="InterstitialReady"/> may have changed.</summary>
        event Action ReadinessChanged;

        /// <summary>True once the ad SDK finished initializing (always false for the null service).</summary>
        bool IsInitialized { get; }

        /// <summary>True while a full-screen ad is on screen (or being opened).</summary>
        bool IsShowing { get; }

        /// <summary>
        /// Whether interstitials may be shown. False once "Remove Ads" is owned (<see cref="SetAdsRemoved"/>).
        /// Rewarded ads are opt-in and stay available regardless.
        /// </summary>
        bool Enabled { get; }

        /// <summary>True when a rewarded ad can be shown right now.</summary>
        bool RewardedReady { get; }

        /// <summary>True when an interstitial can be shown right now (implies <see cref="Enabled"/>).</summary>
        bool InterstitialReady { get; }

        /// <summary>True for the Editor/development simulation (no real ad network).</summary>
        bool IsSimulated { get; }

        /// <summary>
        /// Applies the consent choice and starts the SDK (idempotent; later calls only update consent).
        /// <paramref name="personalizedConsent"/> = the player agreed to personalized ads (GDPR/CCPA).
        /// </summary>
        void Initialize(bool personalizedConsent);

        /// <summary>Updates the personalized-ads consent at any time (for example from the settings screen).</summary>
        void SetConsent(bool personalizedConsent);

        /// <summary>Call with true once "Remove Ads" is owned; disables interstitials.</summary>
        void SetAdsRemoved(bool removed);

        /// <summary>
        /// Shows a rewarded ad. <paramref name="onComplete"/> receives true only if the ad was watched to the end
        /// (grant the reward then); false when skipped, failed, not ready, already showing or timed out.
        /// </summary>
        void ShowRewarded(Action<bool> onComplete);

        /// <summary>
        /// Shows an interstitial if one is ready and ads are enabled; <paramref name="onClosed"/> is always
        /// invoked afterwards (also immediately-next-frame when nothing was shown).
        /// </summary>
        void ShowInterstitial(Action onClosed);
    }
}

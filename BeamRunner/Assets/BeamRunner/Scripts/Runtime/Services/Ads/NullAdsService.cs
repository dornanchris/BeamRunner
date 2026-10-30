using System;

namespace BeamRunner.Services
{
    /// <summary>
    /// Ads service for builds without an ad network (release builds without LevelPlay ids, desktop, WebGL).
    /// Never ready; show requests call back next frame with "not completed" / "closed".
    /// </summary>
    public sealed class NullAdsService : AdsServiceBase
    {
        /// <inheritdoc />
        public override bool IsInitialized => false;

        /// <inheritdoc />
        public override bool RewardedReady => false;

        /// <inheritdoc />
        public override bool InterstitialReady => false;

        /// <inheritdoc />
        public override void Initialize(bool personalizedConsent)
        {
        }

        /// <inheritdoc />
        public override void SetConsent(bool personalizedConsent)
        {
        }

        /// <inheritdoc />
        public override void ShowRewarded(Action<bool> onComplete)
        {
            DeliverRewardedLater(onComplete, false);
        }

        /// <inheritdoc />
        public override void ShowInterstitial(Action onClosed)
        {
            DeliverClosedLater(onClosed);
        }
    }
}

using System;

namespace BeamRunner.Services
{
    /// <summary>
    /// Stand-in ad network for the Editor (always) and for development builds without LevelPlay ids for the
    /// platform (when <c>simulateAdsInEditor</c> is on). Ads "load" after a second and are shown as a full-screen uGUI
    /// overlay (<see cref="SimulatedAdOverlay"/>) with a 3-second countdown and Skip/Close buttons, so the whole
    /// reward / interstitial flow — including audio muting and pausing — can be exercised without a network.
    /// Works while <c>Time.timeScale == 0</c>.
    /// </summary>
    public sealed class SimulatedAdsService : AdsServiceBase
    {
        private const float LoadDelaySeconds = 1f;
        private const float CountdownSeconds = 3f;

        private bool _initialized;
        private bool _rewardedLoaded;
        private bool _interstitialLoaded;
        private int _rewardedTimer;
        private int _interstitialTimer;

        /// <inheritdoc />
        public override bool IsInitialized => _initialized;

        /// <inheritdoc />
        public override bool IsSimulated => true;

        /// <inheritdoc />
        public override bool RewardedReady => _initialized && _rewardedLoaded && !IsShowing;

        /// <inheritdoc />
        public override bool InterstitialReady => Enabled && _initialized && _interstitialLoaded && !IsShowing;

        /// <inheritdoc />
        public override void Initialize(bool personalizedConsent)
        {
            if (_initialized)
            {
                return;
            }

            _initialized = true;
            ServiceLog.Info("Ads: using SIMULATED ads (Editor, or no LevelPlay ids configured for this platform).");
            ScheduleLoad(true);
            ScheduleLoad(false);
        }

        /// <inheritdoc />
        public override void SetConsent(bool personalizedConsent)
        {
        }

        /// <inheritdoc />
        public override void ShowRewarded(Action<bool> onComplete)
        {
            if (!RewardedReady)
            {
                DeliverRewardedLater(onComplete, false);
                return;
            }

            _rewardedLoaded = false;
            Present(true, completed => InvokeRewarded(onComplete, completed));
        }

        /// <inheritdoc />
        public override void ShowInterstitial(Action onClosed)
        {
            if (!InterstitialReady)
            {
                DeliverClosedLater(onClosed);
                return;
            }

            _interstitialLoaded = false;
            Present(false, _ => InvokeClosed(onClosed));
        }

        private void Present(bool rewarded, Action<bool> done)
        {
            BeginShow();
            bool finished = false;
            Action<bool> finish = completed =>
            {
                if (finished)
                {
                    return;
                }

                finished = true;
                EndShow();
                ScheduleLoad(rewarded);
                done(completed);
            };

            SimulatedAdOverlay overlay = SimulatedAdOverlay.Create(rewarded, CountdownSeconds, finish);
            if (overlay == null)
            {
                ServiceLog.Warn("Ads: simulated overlay could not be created; reporting the ad as not completed.");
                ServiceRunner.Post(() => finish(false));
            }
        }

        private void ScheduleLoad(bool rewarded)
        {
            if (rewarded)
            {
                if (_rewardedTimer != 0 || _rewardedLoaded)
                {
                    return;
                }

                _rewardedTimer = ServiceRunner.Delay(LoadDelaySeconds, () =>
                {
                    _rewardedTimer = 0;
                    _rewardedLoaded = true;
                    RaiseReadinessChanged();
                });
            }
            else
            {
                if (_interstitialTimer != 0 || _interstitialLoaded)
                {
                    return;
                }

                _interstitialTimer = ServiceRunner.Delay(LoadDelaySeconds, () =>
                {
                    _interstitialTimer = 0;
                    _interstitialLoaded = true;
                    RaiseReadinessChanged();
                });
            }
        }
    }
}

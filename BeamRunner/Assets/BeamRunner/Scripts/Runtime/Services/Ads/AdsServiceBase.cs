using System;

namespace BeamRunner.Services
{
    /// <summary>
    /// Shared plumbing for <see cref="IAdsService"/> implementations: showing state, Remove-Ads flag,
    /// exception-safe event raising and deferred callback delivery.
    /// </summary>
    public abstract class AdsServiceBase : IAdsService
    {
        private bool _adsRemoved;

        /// <inheritdoc />
        public event Action<bool> AdShowingChanged;

        /// <inheritdoc />
        public event Action ReadinessChanged;

        /// <inheritdoc />
        public abstract bool IsInitialized { get; }

        /// <inheritdoc />
        public bool IsShowing { get; private set; }

        /// <inheritdoc />
        public bool Enabled => !_adsRemoved;

        /// <inheritdoc />
        public abstract bool RewardedReady { get; }

        /// <inheritdoc />
        public abstract bool InterstitialReady { get; }

        /// <inheritdoc />
        public virtual bool IsSimulated => false;

        /// <inheritdoc />
        public abstract void Initialize(bool personalizedConsent);

        /// <inheritdoc />
        public abstract void SetConsent(bool personalizedConsent);

        /// <inheritdoc />
        public void SetAdsRemoved(bool removed)
        {
            if (_adsRemoved == removed)
            {
                return;
            }

            _adsRemoved = removed;
            ServiceLog.Info(removed ? "Ads: interstitials disabled (Remove Ads owned)." : "Ads: interstitials enabled.");
            OnAdsRemovedChanged(removed);
            RaiseReadinessChanged();
        }

        /// <inheritdoc />
        public abstract void ShowRewarded(Action<bool> onComplete);

        /// <inheritdoc />
        public abstract void ShowInterstitial(Action onClosed);

        /// <summary>Hook for implementations that stop/start preloading interstitials.</summary>
        protected virtual void OnAdsRemovedChanged(bool removed)
        {
        }

        /// <summary>Marks an ad as on screen and notifies listeners.</summary>
        protected void BeginShow()
        {
            if (IsShowing)
            {
                return;
            }

            IsShowing = true;
            RaiseShowingChanged(true);
            RaiseReadinessChanged();
        }

        /// <summary>Marks the ad as gone and notifies listeners.</summary>
        protected void EndShow()
        {
            if (!IsShowing)
            {
                return;
            }

            IsShowing = false;
            RaiseShowingChanged(false);
            RaiseReadinessChanged();
        }

        /// <summary>Raises <see cref="ReadinessChanged"/>, isolating subscriber exceptions.</summary>
        protected void RaiseReadinessChanged()
        {
            Action handler = ReadinessChanged;
            if (handler == null)
            {
                return;
            }

            try
            {
                handler();
            }
            catch (Exception e)
            {
                ServiceLog.Exception("Ads ReadinessChanged handler failed", e);
            }
        }

        /// <summary>Invokes a rewarded callback now, isolating exceptions.</summary>
        protected static void InvokeRewarded(Action<bool> callback, bool completed)
        {
            if (callback == null)
            {
                return;
            }

            try
            {
                callback(completed);
            }
            catch (Exception e)
            {
                ServiceLog.Exception("Rewarded ad callback failed", e);
            }
        }

        /// <summary>Invokes an interstitial callback now, isolating exceptions.</summary>
        protected static void InvokeClosed(Action callback)
        {
            if (callback == null)
            {
                return;
            }

            try
            {
                callback();
            }
            catch (Exception e)
            {
                ServiceLog.Exception("Interstitial callback failed", e);
            }
        }

        /// <summary>Delivers a rewarded result on the next frame (used when nothing is shown).</summary>
        protected static void DeliverRewardedLater(Action<bool> callback, bool completed)
        {
            if (callback != null)
            {
                ServiceRunner.Post(() => InvokeRewarded(callback, completed));
            }
        }

        /// <summary>Delivers an interstitial close on the next frame (used when nothing is shown).</summary>
        protected static void DeliverClosedLater(Action callback)
        {
            if (callback != null)
            {
                ServiceRunner.Post(() => InvokeClosed(callback));
            }
        }

        private void RaiseShowingChanged(bool showing)
        {
            Action<bool> handler = AdShowingChanged;
            if (handler == null)
            {
                return;
            }

            try
            {
                handler(showing);
            }
            catch (Exception e)
            {
                ServiceLog.Exception("AdShowingChanged handler failed", e);
            }
        }
    }
}

using System;
using BeamRunner.Core.Localization;
using BeamRunner.Services;

namespace BeamRunner.Game
{
    /// <summary>Ads, in-app purchases and consent wiring.</summary>
    public sealed partial class GameController
    {
        private bool _consentPending;
        private bool _servicesInitialized;

        /// <summary>True when ads are backed by a real or simulated network (consent is meaningful).</summary>
        public bool AdsAvailable
        {
            get { return Ads != null && !(Ads is NullAdsService); }
        }

        /// <summary>True when the store can sell things right now.</summary>
        public bool StoreAvailable
        {
            get { return Iap != null && Iap.Available; }
        }

        partial void InitializeServices()
        {
            Config = ServiceHub.Config;
            Ads = ServiceHub.Ads;
            Iap = ServiceHub.Iap;
            InitializeEngagement(); // reminders, review prompt, crash-report consent (GameController.Engagement.cs)
            InitializeOnline(); // Play Games / Game Center / cloud save wiring; started in StartServices (GameController.Online.cs)

            // Subscribe BEFORE initialising the store so restored / deferred transactions are granted.
            Iap.EntitlementGranted += OnEntitlementGranted;
            Iap.AvailabilityChanged += OnStoreAvailabilityChanged;
            Ads.AdShowingChanged += OnAdShowingChanged;

            if (AdsAvailable && !Profile.Profile.Flags.ConsentAsked)
            {
                // Ask on the main menu, once the player can actually see the dialog.
                _consentPending = true;
                return;
            }

            StartServices(Profile.Profile.Flags.PersonalizedAds);
        }

        private void StartServices(bool personalizedAds)
        {
            StartOnline(); // once, after the consent flow (idempotent)
            if (_servicesInitialized)
            {
                ServiceHub.SetAdsConsent(personalizedAds);
                return;
            }

            _servicesInitialized = true;
            ServiceHub.Initialize(personalizedAds, Profile.AdsRemoved);
        }

        /// <summary>Called when the menu first appears; shows the consent dialog if still needed.</summary>
        private void RequestConsentIfPending()
        {
            if (!_consentPending)
            {
                return;
            }

            _consentPending = false;
            try
            {
                RaiseConsentRequested(personalized =>
                {
                    CommitDialogCrashReportsChoice(); // the dialog's "Send crash reports" switch
                    SetConsent(personalized);
                });
            }
            catch (Exception e)
            {
                // Without this the store and ads would never start this session (they wait for the answer).
                // Start them non-personalised without recording an answer, so the dialog is offered next launch.
                FieldLog.Exception("Showing the consent dialog", e);
                StartServices(false);
            }
        }

        /// <summary>Records the ads-personalisation choice (first-launch dialog or Settings › Privacy).</summary>
        public void SetConsent(bool personalized)
        {
            Profile.SetConsent(personalized);
            Store.SaveNow(true);
            StartServices(personalized);
        }

        /// <summary>Starts an IAP purchase. Entitlements are granted via <see cref="OnEntitlementGranted"/>.</summary>
        public void BuyProduct(string productId, Action<PurchaseResult> onDone)
        {
            if (Iap == null)
            {
                onDone?.Invoke(PurchaseResult.Unavailable(productId, "Store unavailable"));
                return;
            }

            Iap.Purchase(productId, result =>
            {
                if (!result.IsSuccess && result.Status != PurchaseStatus.Cancelled &&
                    result.Status != PurchaseStatus.Pending)
                {
                    PresentError();
                    RaiseNotify(result.Status == PurchaseStatus.Unavailable
                        ? Loc.T("store.unavailable_toast")
                        : Loc.T("store.purchase_failed"));
                }
                else if (result.Status == PurchaseStatus.Pending)
                {
                    RaiseNotify(Loc.T("store.purchase_pending"));
                }

                onDone?.Invoke(result);
            });
        }

        public void RestorePurchases(Action<bool> onDone)
        {
            if (Iap == null || !Iap.SupportsRestore)
            {
                onDone?.Invoke(false);
                return;
            }

            Iap.Restore(ok =>
            {
                RaiseNotify(ok ? Loc.T("store.restored") : Loc.T("store.restore_failed"));
                onDone?.Invoke(ok);
            });
        }

        /// <summary>Price label for a product (localized when the store is up, config fallback otherwise).</summary>
        public string GetPrice(string productId)
        {
            return Iap != null ? Iap.GetPrice(productId) : string.Empty;
        }

        private void OnEntitlementGranted(string productId)
        {
            IapProductConfig product = Config != null ? Config.FindProduct(productId) : null;
            bool removesAds = productId == ProductIds.RemoveAds;
            long gems = product != null && product.IsConsumable ? product.Gems : 0;

            // The store re-reports owned non-consumables every time it connects (UnityIapService.Flush syncs
            // them once per session, and again on restore). Granting is idempotent, but the purchase sound and
            // the "Ads removed. Thank you…" toast must only play when something actually changed — otherwise
            // every Remove-Ads owner hears/sees them at each app launch.
            bool newlyRemovedAds = removesAds && !Profile.AdsRemoved;
            Profile.GrantEntitlement(productId, removesAds, gems);
            if (removesAds)
            {
                Ads?.SetAdsRemoved(true);
            }

            if (!newlyRemovedAds && gems <= 0)
            {
                return;
            }

            Store.SaveNow(true);
            PresentPurchase();
            if (newlyRemovedAds)
            {
                RaiseNotify(Loc.T("store.ads_removed"));
            }
            else
            {
                RaiseNotify(Loc.T("store.gems_added", ("n", gems)));
            }
        }

        private void OnStoreAvailabilityChanged()
        {
            if (Iap != null && Iap.Available && Iap.Owns(ProductIds.RemoveAds) && !Profile.AdsRemoved)
            {
                OnEntitlementGranted(ProductIds.RemoveAds);
            }
        }

        private void OnAdShowingChanged(bool showing)
        {
            // Silence the game while a full-screen ad plays; the ad SDK has its own audio.
            UnityEngine.AudioListener.pause = showing;
        }
    }
}

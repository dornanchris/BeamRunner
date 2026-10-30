using System;

namespace BeamRunner.Services
{
    /// <summary>
    /// In-app purchases. Main thread only; callbacks/events are raised on the main thread and purchase/restore
    /// callbacks are never invoked synchronously from inside the call. Never throws into gameplay.
    /// </summary>
    /// <remarks>
    /// <b>Granting.</b> <see cref="EntitlementGranted"/> is the single place where the game grants what was
    /// bought (add gems, set the Remove-Ads flag) and immediately persists its profile. It fires for purchases
    /// made through <see cref="Purchase"/>, for purchases that complete later (deferred / pending payments,
    /// interrupted transactions finished on the next launch), for restored purchases, and — idempotently — for
    /// non-consumables the store reports as owned. The <see cref="Purchase"/> callback is for UI feedback only;
    /// never grant from it. A consumable transaction is delivered at most once (deduplicated by transaction id);
    /// a non-consumable may be reported more than once and granting it must be idempotent. Subscribe before
    /// <see cref="Initialize"/>; transactions that arrive while nobody is subscribed are held open and delivered
    /// as soon as a handler subscribes.
    /// </remarks>
    public interface IIapService
    {
        /// <summary>Raised when <see cref="Available"/>, <see cref="Initializing"/>, prices or ownership may have changed.</summary>
        event Action AvailabilityChanged;

        /// <summary>
        /// Raised with the product id each time a purchase must be granted (see remarks). Handlers must grant and
        /// persist synchronously.
        /// </summary>
        event Action<string> EntitlementGranted;

        /// <summary>True when the store is connected and products can be bought.</summary>
        bool Available { get; }

        /// <summary>True while the store connection is being established.</summary>
        bool Initializing { get; }

        /// <summary>True while a <see cref="Purchase"/> is waiting for the store.</summary>
        bool PurchaseInProgress { get; }

        /// <summary>
        /// True where a "Restore Purchases" button is required (Apple platforms). On Google Play purchases are
        /// restored automatically when the store connects.
        /// </summary>
        bool SupportsRestore { get; }

        /// <summary>Connects to the store (idempotent; after a failure, a later call retries after a cool-down).</summary>
        void Initialize();

        /// <summary>
        /// Localized price string from the store (e.g. "€2,99"), or the <c>fallbackPrice</c> from
        /// <c>monetization.json</c> while the store is unavailable and in the Editor. May be empty.
        /// </summary>
        string GetPrice(string productId);

        /// <summary>True if the store reports <paramref name="productId"/> (a non-consumable) as owned.</summary>
        bool Owns(string productId);

        /// <summary>Starts a purchase; <paramref name="onDone"/> receives the outcome for UI feedback.</summary>
        void Purchase(string productId, Action<PurchaseResult> onDone);

        /// <summary>
        /// Restores previous purchases (Apple: asks the App Store; Android: refreshes the owned-purchases list).
        /// Restored items arrive through <see cref="EntitlementGranted"/> before <paramref name="onDone"/>(true).
        /// </summary>
        void Restore(Action<bool> onDone);
    }
}

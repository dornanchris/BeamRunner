using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Purchasing;

namespace BeamRunner.Services
{
    /// <summary>
    /// Unity IAP 5 implementation (coded <see cref="StoreController"/> API; no codeless IAP, no legacy
    /// <c>IStoreListener</c>). Google Play (Play Billing Library 9) on Android, the App Store (StoreKit 2) on iOS and
    /// Unity's fake store in the Editor (a Buy/Cancel dialog; prices shown are the config fallbacks).
    /// </summary>
    /// <remarks>
    /// <para><b>Flow.</b> <see cref="Initialize"/> → <c>UnityIAPServices.StoreController()</c>, subscribe to every store
    /// event, <c>Connect()</c> → <c>OnStoreConnected</c> → <c>FetchProducts</c> (catalogue from
    /// <see cref="MonetizationConfig"/>) → <c>OnProductsFetched</c> (store available) → <c>FetchPurchases</c>, which
    /// re-delivers unconfirmed purchases through <c>OnPurchasePending</c> and lists owned non-consumables as
    /// <see cref="ConfirmedOrder"/>s. Unity Gaming Services are not initialized: IAP 5 has its own initialization
    /// path and this project uses no UGS service that needs it.</para>
    /// <para><b>Granting.</b> Every <see cref="PendingOrder"/> (new purchase, Ask-to-Buy / pending payment approved
    /// later, purchase interrupted before confirmation, restored purchase) goes through
    /// <see cref="HandlePendingOrder"/>: it raises <see cref="EntitlementGranted"/> — consumables at most once per
    /// transaction id (session set + the persistent <see cref="GrantLedger"/>) — and only after the handlers returned
    /// calls <c>ConfirmPurchase</c> (Google: consume / acknowledge; Apple: finish). A confirmation that fails is
    /// retried when the store reconnects; if the app dies first, the store re-delivers the order next launch and the
    /// ledger prevents a second grant. Orders that arrive before anyone subscribed to
    /// <see cref="EntitlementGranted"/> are held unconfirmed and processed when a handler subscribes. Owned
    /// non-consumables reported as <see cref="ConfirmedOrder"/>s are re-announced once per session (and on
    /// restore). Receipt validation is intentionally out of scope.</para>
    /// </remarks>
    public sealed class UnityIapService : IIapService
    {
        // Timeouts in active app time (paused while a store sheet suspends the app); back-off in real time.
        private const float ConnectWatchdogSeconds = 45f;
        private const float ProductsFetchTimeoutSeconds = 60f;
        private const float PurchasesFetchTimeoutSeconds = 30f;
        private const float PurchaseTimeoutSeconds = 180f;
        private const float RestoreTimeoutSeconds = 90f;
        private const float RestoreFetchWaitSeconds = 20f;
        private const float ExplicitRetryCooldownSeconds = 10f;
        private const float PurchasesFetchRetrySeconds = 15f;
        private const int MaxPurchasesFetchRetries = 3;
        private const float ConfirmRetrySeconds = 5f;
        private const int MaxConfirmAttempts = 4;

        private static readonly float[] RetryBackoffSeconds = { 5f, 10f, 20f, 40f, 80f, 160f, 300f };

        private readonly MonetizationConfig _config;
        private readonly GrantLedger _ledger;
        private readonly List<PendingOrder> _held = new List<PendingOrder>();
        private readonly HashSet<string> _handledTransactions = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, ConfirmEntry> _unconfirmed =
            new Dictionary<string, ConfirmEntry>(StringComparer.Ordinal);
        private readonly HashSet<string> _syncedNonConsumables = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _ownedThisSession = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<Action<bool>> _restoreCallbacks = new List<Action<bool>>();

        private StoreController _store;
        private bool _storeFailed;
        private IRetryPolicy _productsRetryPolicy;
        private Action<string> _entitlementGranted;
        private InFlightPurchase _inFlight;

        private bool _connected;
        private bool _connectInFlight;
        private bool _productsReady;
        private bool _productsFetching;
        private bool _productsFetchedThisRound;
        private bool _purchasesFetching;
        private bool _showConnecting;
        private bool _attempted;
        private float _lastAttemptAt;
        private int _retryIndex;
        private int _purchasesFetchRetries;

        private int _connectWatchdogTimer;
        private int _retryTimer;
        private int _productsFetchTimer;
        private int _purchasesFetchTimer;
        private int _purchasesFetchRetryTimer;
        private int _confirmRetryTimer;
        private bool _flushScheduled;

        private bool _restoreRunning;
        private bool _restoreAwaitingFetch;
        private int _restoreTimer;
        private int _restoreFetchTimer;

        /// <summary>Creates the service. Nothing touches the store until <see cref="Initialize"/>.</summary>
        public UnityIapService(MonetizationConfig config)
        {
            _config = config ?? MonetizationConfig.Current;
            _ledger = new GrantLedger();
        }

        /// <inheritdoc />
        public event Action AvailabilityChanged;

        /// <inheritdoc />
        public event Action<string> EntitlementGranted
        {
            add
            {
                _entitlementGranted += value;
                if (value != null)
                {
                    ScheduleFlush();
                }
            }
            remove
            {
                _entitlementGranted -= value;
            }
        }

        /// <inheritdoc />
        public bool Available => _connected && _productsReady;

        /// <inheritdoc />
        public bool Initializing => _showConnecting && !Available;

        /// <inheritdoc />
        public bool PurchaseInProgress => _inFlight != null;

        /// <inheritdoc />
        public bool SupportsRestore
        {
            get
            {
#if UNITY_IOS
                return true;
#else
                return false;
#endif
            }
        }

        private static bool IsAndroidPlayer => Application.platform == RuntimePlatform.Android;

        // ------------------------------------------------------------------------------ connection

        /// <inheritdoc />
        public void Initialize()
        {
            if (!EnsureStore() || Available || _connectInFlight || _productsFetching)
            {
                return;
            }

            if (_attempted && Time.realtimeSinceStartup - _lastAttemptAt < ExplicitRetryCooldownSeconds)
            {
                ScheduleRetry();
                return;
            }

            CancelTimer(ref _retryTimer);
            Progress(true);
        }

        /// <summary>Creates the store controller and subscribes to all of its events (before any Connect).</summary>
        private bool EnsureStore()
        {
            if (_store != null)
            {
                return true;
            }

            if (_storeFailed)
            {
                return false;
            }

            try
            {
                StoreController store = UnityIAPServices.StoreController();
                store.OnStoreConnected += OnStoreConnectedEvent;
                store.OnStoreDisconnected += OnStoreDisconnectedEvent;
                store.OnProductsFetched += OnProductsFetchedEvent;
                store.OnProductsFetchFailed += OnProductsFetchFailedEvent;
                store.OnPurchasesFetched += OnPurchasesFetchedEvent;
                store.OnPurchasesFetchFailed += OnPurchasesFetchFailedEvent;
                store.OnPurchasePending += OnPurchasePendingEvent;
                store.OnPurchaseConfirmed += OnPurchaseConfirmedEvent;
                store.OnPurchaseFailed += OnPurchaseFailedEvent;
                store.OnPurchaseDeferred += OnPurchaseDeferredEvent;

                // Quick in-attempt retries inside IAP (1.5 s, 3 s, 6 s); longer back-off is handled here.
                _productsRetryPolicy = new AggregateRetryPolicy(new MaximumNumberOfAttemptsRetryPolicy(3),
                    new ExponentialBackOffRetryPolicy(1500, 8000, 2f));
                _store = store;
                ServiceRunner.PauseChanged += OnAppPauseChanged;
                return true;
            }
            catch (Exception e)
            {
                ServiceLog.Exception("IAP: creating the store controller failed; store disabled", e);
                _storeFailed = true;
                _store = null;
                RaiseAvailabilityChanged();
                return false;
            }
        }

        /// <summary>Takes the next step towards "connected with products": connect, or fetch the catalogue.</summary>
        private void Progress(bool visible)
        {
            _attempted = true;
            _lastAttemptAt = Time.realtimeSinceStartup;
            if (!_connected)
            {
                StartConnect(visible);
            }
            else if (!_productsReady)
            {
                StartProductsFetch(visible);
            }
        }

        private void StartConnect(bool visible)
        {
            if (_connectInFlight || _store == null)
            {
                return;
            }

            _connectInFlight = true;
            if (visible)
            {
                _showConnecting = true;
            }

            RaiseAvailabilityChanged();
            CancelTimer(ref _connectWatchdogTimer);
            _connectWatchdogTimer = ServiceRunner.Delay(ConnectWatchdogSeconds, OnConnectWatchdog, true);
            try
            {
                ServiceLog.Info("IAP: connecting to the store (" + UnityIAPServices.GetDefaultStore() + ").");
                // Completes on success and on final failure; the outcome arrives through the events.
                ObserveTask(_store.Connect());
            }
            catch (Exception e)
            {
                ServiceLog.Exception("IAP: Connect failed", e);
                HandleDisconnected("exception: " + e.Message);
            }
        }

        private void OnStoreConnectedEvent()
        {
            Dispatch(HandleConnected, "OnStoreConnected");
        }

        private void HandleConnected()
        {
            _connected = true;
            _connectInFlight = false;
            _purchasesFetchRetries = 0;
            CancelTimer(ref _connectWatchdogTimer);
            ServiceLog.Info("IAP: store connected.");

            foreach (ConfirmEntry entry in _unconfirmed.Values)
            {
                entry.Failures = 0;
            }

            if (_productsReady)
            {
                _showConnecting = false;
                FetchPurchases();
            }
            else
            {
                StartProductsFetch(_showConnecting);
            }

            RetryConfirmations();
            RaiseAvailabilityChanged();
        }

        private void OnStoreDisconnectedEvent(StoreConnectionFailureDescription description)
        {
            string message = description != null ? description.Message : "";
            Dispatch(() => HandleDisconnected(message), "OnStoreDisconnected");
        }

        private void HandleDisconnected(string reason)
        {
            _connected = false;
            _connectInFlight = false;
            _showConnecting = false;
            CancelTimer(ref _connectWatchdogTimer);
            foreach (ConfirmEntry entry in _unconfirmed.Values)
            {
                entry.InFlight = false; // re-sent after reconnecting
            }

            ServiceLog.Warn("IAP: store unavailable (" + (string.IsNullOrEmpty(reason) ? "disconnected" : reason) + ").");
            RaiseAvailabilityChanged();
            ScheduleRetry();
        }

        private void OnConnectWatchdog()
        {
            _connectWatchdogTimer = 0;
            if (!_connectInFlight)
            {
                return;
            }

            // Stop showing "connecting…"; a late OnStoreConnected still makes the store available.
            _connectInFlight = false;
            _showConnecting = false;
            ServiceLog.Warn("IAP: the store is taking unusually long to connect.");
            RaiseAvailabilityChanged();
            ScheduleRetry();
        }

        private void ScheduleRetry()
        {
            if (_retryTimer != 0 || _store == null)
            {
                return;
            }

            float delay = RetryBackoffSeconds[Math.Min(_retryIndex, RetryBackoffSeconds.Length - 1)];
            _retryIndex++;
            _retryTimer = ServiceRunner.Delay(delay, OnRetryTimer);
            ServiceLog.Info("IAP: retrying the store in " + delay + " s.");
        }

        private void OnRetryTimer()
        {
            _retryTimer = 0;
            if (Available || _connectInFlight || _productsFetching)
            {
                return;
            }

            Progress(false);
        }

        private void OnAppPauseChanged(bool paused)
        {
            // Back from the background (maybe back online): try again now instead of waiting for the back-off.
            if (paused || Available || _connectInFlight || _productsFetching || _store == null)
            {
                return;
            }

            if (Time.realtimeSinceStartup - _lastAttemptAt < ExplicitRetryCooldownSeconds)
            {
                return;
            }

            CancelTimer(ref _retryTimer);
            Progress(false);
        }

        // ------------------------------------------------------------------------------ products

        private void StartProductsFetch(bool visible)
        {
            if (_productsFetching || !_connected || _store == null)
            {
                return;
            }

            List<ProductDefinition> catalogue = BuildCatalogue();
            if (catalogue.Count == 0)
            {
                ServiceLog.Warn("IAP: the catalogue is empty; nothing to fetch.");
                return;
            }

            _productsFetching = true;
            _productsFetchedThisRound = false;
            if (visible && !_showConnecting)
            {
                _showConnecting = true;
                RaiseAvailabilityChanged();
            }

            ServiceLog.Info("IAP: fetching " + catalogue.Count + " products.");
            CancelTimer(ref _productsFetchTimer);
            _productsFetchTimer = ServiceRunner.Delay(ProductsFetchTimeoutSeconds, OnProductsFetchTimeout, true);
            try
            {
                _store.FetchProducts(catalogue, _productsRetryPolicy);
            }
            catch (Exception e)
            {
                ServiceLog.Exception("IAP: FetchProducts failed", e);
                HandleProductsFetchFailure("exception: " + e.Message);
            }
        }

        private List<ProductDefinition> BuildCatalogue()
        {
            var definitions = new List<ProductDefinition>();
            IReadOnlyList<IapProductConfig> products = _config.Products;
            for (int i = 0; i < products.Count; i++)
            {
                IapProductConfig product = products[i];
                if (product == null || string.IsNullOrEmpty(product.Id))
                {
                    continue;
                }

                definitions.Add(new ProductDefinition(product.Id,
                    product.IsConsumable ? ProductType.Consumable : ProductType.NonConsumable));
            }

            return definitions;
        }

        private void OnProductsFetchedEvent(List<Product> products)
        {
            int count = products != null ? products.Count : 0;
            Dispatch(() => HandleProductsFetched(count), "OnProductsFetched");
        }

        private void HandleProductsFetched(int count)
        {
            if (count <= 0)
            {
                HandleProductsFetchFailure("the store returned no products");
                return;
            }

            _productsFetchedThisRound = true;
            _productsFetching = false;
            _productsReady = true;
            _showConnecting = false;
            _retryIndex = 0;
            CancelTimer(ref _productsFetchTimer);
            CancelTimer(ref _retryTimer);
            ServiceLog.Info("IAP: store ready (" + count + " products).");
            FetchPurchases();
            RaiseAvailabilityChanged();
        }

        private void OnProductsFetchFailedEvent(ProductFetchFailed failure)
        {
            string reason = failure != null ? failure.FailureReason : "";
            string missing = "";
            if (failure != null && failure.FailedFetchProducts != null)
            {
                List<ProductDefinition> failed = failure.FailedFetchProducts;
                for (int i = 0; i < failed.Count; i++)
                {
                    if (failed[i] != null)
                    {
                        missing += (missing.Length > 0 ? ", " : "") + failed[i].id;
                    }
                }
            }

            Dispatch(() =>
            {
                if (_productsFetchedThisRound && !_productsFetching)
                {
                    // Partial success: the rest of the catalogue is usable.
                    ServiceLog.Warn("IAP: products missing from the store (check their ids / activation): " + missing +
                                    " (" + reason + ").");
                    return;
                }

                HandleProductsFetchFailure(reason + (missing.Length > 0 ? " [" + missing + "]" : ""));
            }, "OnProductsFetchFailed");
        }

        private void OnProductsFetchTimeout()
        {
            _productsFetchTimer = 0;
            if (_productsFetching)
            {
                HandleProductsFetchFailure("timed out");
            }
        }

        private void HandleProductsFetchFailure(string reason)
        {
            _productsFetching = false;
            _showConnecting = false;
            CancelTimer(ref _productsFetchTimer);
            ServiceLog.Warn("IAP: fetching products failed (" + reason + ").");
            if (!_productsReady)
            {
                ScheduleRetry();
            }

            // Deliver interrupted / approved purchases even without a catalogue.
            FetchPurchases();
            RaiseAvailabilityChanged();
        }

        // ------------------------------------------------------------------------------ queries

        /// <inheritdoc />
        public string GetPrice(string productId)
        {
            IapProductConfig entry = _config.FindProduct(productId);
            string fallback = entry != null ? entry.FallbackPrice : "";
            if (_store == null || Application.isEditor)
            {
                return fallback;
            }

            Product product = FindProduct(productId);
            string price = product != null && product.metadata != null ? product.metadata.localizedPriceString : null;
            return string.IsNullOrEmpty(price) ? fallback : price;
        }

        /// <inheritdoc />
        public bool Owns(string productId)
        {
            IapProductConfig entry = _config.FindProduct(productId);
            if (entry == null || entry.IsConsumable)
            {
                return false;
            }

            if (_ownedThisSession.Contains(productId))
            {
                return true;
            }

            if (_store == null)
            {
                return false;
            }

            try
            {
                IReadOnlyList<Order> orders = _store.GetPurchases();
                if (orders == null)
                {
                    return false;
                }

                for (int i = 0; i < orders.Count; i++)
                {
                    Order order = orders[i];
                    if ((order is ConfirmedOrder || order is PendingOrder) && ContainsProduct(order, productId))
                    {
                        return true;
                    }
                }
            }
            catch (Exception e)
            {
                ServiceLog.Warn("IAP: reading the purchase cache failed: " + e.Message);
            }

            return false;
        }

        private Product FindProduct(string productId)
        {
            if (_store == null || string.IsNullOrEmpty(productId))
            {
                return null;
            }

            try
            {
                return _store.GetProductById(productId);
            }
            catch (Exception)
            {
                return null;
            }
        }

        // ------------------------------------------------------------------------------ purchasing

        /// <inheritdoc />
        public void Purchase(string productId, Action<PurchaseResult> onDone)
        {
            IapProductConfig entry = string.IsNullOrEmpty(productId) ? null : _config.FindProduct(productId);
            if (entry == null)
            {
                Deliver(onDone, PurchaseResult.Failed(productId, "Unknown item."));
                return;
            }

            if (!Available)
            {
                bool connecting = Initializing || _connectInFlight || _productsFetching;
                Initialize();
                Deliver(onDone, PurchaseResult.Unavailable(productId, connecting
                    ? "The store is still connecting. Please try again in a moment."
                    : "The store is not available right now. Check your connection and try again."));
                return;
            }

            if (_inFlight != null)
            {
                Deliver(onDone, PurchaseResult.Failed(productId, "Another purchase is still in progress."));
                return;
            }

            Product product = FindProduct(productId);
            if (product == null || !product.availableToPurchase)
            {
                Deliver(onDone, PurchaseResult.Unavailable(productId, "This item is not available in the store."));
                return;
            }

            if (!entry.IsConsumable && Owns(productId))
            {
                SyncOwned(productId, true);
                Deliver(onDone, PurchaseResult.Succeeded(productId, "Already owned."));
                return;
            }

            var purchase = new InFlightPurchase(productId, onDone);
            _inFlight = purchase;
            purchase.TimerId = ServiceRunner.Delay(PurchaseTimeoutSeconds, () => OnPurchaseTimeout(purchase), true);
            ServiceLog.Info("IAP: purchasing '" + productId + "'.");
            try
            {
                // Outcome: OnPurchasePending (paid), OnPurchaseDeferred (awaiting approval) or OnPurchaseFailed.
                _store.PurchaseProduct(product);
            }
            catch (Exception e)
            {
                ServiceLog.Exception("IAP: PurchaseProduct failed", e);
                CompleteInFlight(productId, PurchaseResult.Failed(productId, "The purchase could not be started."));
            }
        }

        private void OnPurchaseTimeout(InFlightPurchase purchase)
        {
            purchase.TimerId = 0;
            if (_inFlight != purchase)
            {
                return;
            }

            ServiceLog.Warn("IAP: no answer from the store for the purchase of '" + purchase.ProductId + "'.");
            CompleteInFlight(purchase.ProductId, PurchaseResult.Failed(purchase.ProductId,
                "The store did not respond. If you were charged, the item will be delivered automatically."));
        }

        private void OnPurchasePendingEvent(PendingOrder order)
        {
            Dispatch(() => HandlePendingOrder(order), "OnPurchasePending");
        }

        /// <summary>
        /// The single grant path: raises <see cref="EntitlementGranted"/> for the order's products (deduplicated),
        /// then confirms the order. Holds it unconfirmed while nobody can grant.
        /// </summary>
        private void HandlePendingOrder(PendingOrder order)
        {
            if (order == null)
            {
                return;
            }

            string transactionId = TransactionIdOf(order);
            List<string> productIds = KnownProductIds(order);
            if (productIds.Count == 0)
            {
                // Not ours to grant. Left unconfirmed on purpose: Google Play refunds unacknowledged purchases
                // after three days; Apple simply re-delivers the transaction.
                ServiceLog.Warn("IAP: ignoring a purchase of an unknown product ('" + FirstProductId(order) +
                                "'); it is left unconfirmed.");
                return;
            }

            if (_entitlementGranted == null)
            {
                Hold(order, transactionId);
                return;
            }

            RemoveHeld(transactionId);
            if (transactionId.Length == 0 || !_handledTransactions.Contains(transactionId))
            {
                Grant(productIds, transactionId);
                if (transactionId.Length > 0)
                {
                    _handledTransactions.Add(transactionId);
                }
            }
            else
            {
                ServiceLog.Info("IAP: transaction for '" + productIds[0] + "' already handled this session.");
            }

            for (int i = 0; i < productIds.Count; i++)
            {
                CompleteInFlight(productIds[i], PurchaseResult.Succeeded(productIds[i]));
            }

            // Only now, with every EntitlementGranted handler done (and the profile saved), finish the transaction.
            Confirm(order);
            RaiseAvailabilityChanged();
        }

        private void Grant(List<string> productIds, string transactionId)
        {
            bool alreadyGranted = transactionId.Length > 0 && _ledger.Contains(transactionId);
            bool grantedConsumable = false;
            for (int i = 0; i < productIds.Count; i++)
            {
                string productId = productIds[i];
                IapProductConfig entry = _config.FindProduct(productId);
                if (entry != null && entry.IsConsumable)
                {
                    if (alreadyGranted)
                    {
                        ServiceLog.Info("IAP: transaction for '" + productId + "' was already granted; skipping.");
                        continue;
                    }

                    ServiceLog.Info("IAP: granting '" + productId + "'.");
                    RaiseEntitlementGranted(productId);
                    grantedConsumable = true;
                }
                else
                {
                    ServiceLog.Info("IAP: granting '" + productId + "'.");
                    _ownedThisSession.Add(productId);
                    _syncedNonConsumables.Add(productId);
                    RaiseEntitlementGranted(productId);
                }
            }

            if (grantedConsumable)
            {
                _ledger.Add(transactionId);
            }
        }

        private void Hold(PendingOrder order, string transactionId)
        {
            RemoveHeld(transactionId);
            _held.Add(order);
            ServiceLog.Warn("IAP: purchase of '" + FirstProductId(order) + "' arrived before an EntitlementGranted " +
                            "handler was subscribed; holding it unconfirmed.");
        }

        private void RemoveHeld(string transactionId)
        {
            if (transactionId.Length == 0)
            {
                return;
            }

            for (int i = _held.Count - 1; i >= 0; i--)
            {
                if (string.Equals(TransactionIdOf(_held[i]), transactionId, StringComparison.Ordinal))
                {
                    _held.RemoveAt(i);
                }
            }
        }

        private bool IsHeld(string transactionId)
        {
            for (int i = 0; i < _held.Count; i++)
            {
                if (string.Equals(TransactionIdOf(_held[i]), transactionId, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private void OnPurchaseFailedEvent(FailedOrder order)
        {
            Dispatch(() => HandlePurchaseFailed(order), "OnPurchaseFailed");
        }

        private void HandlePurchaseFailed(FailedOrder order)
        {
            string failedId = FirstProductId(order);
            PurchaseFailureReason reason = order != null ? order.FailureReason : PurchaseFailureReason.Unknown;
            string details = order != null ? order.Details : "";
            ServiceLog.Info("IAP: purchase of '" + failedId + "' failed: " + reason + " " + details);

            if (reason == PurchaseFailureReason.StoreNotConnected && _connected)
            {
                HandleDisconnected("the store reported it is not connected");
            }

            InFlightPurchase purchase = _inFlight;
            if (purchase == null)
            {
                return;
            }

            // Failures for unknown/invalid products (e.g. "InvalidProduct") belong to the purchase in flight.
            if (!string.IsNullOrEmpty(failedId) && _config.FindProduct(failedId) != null &&
                !string.Equals(failedId, purchase.ProductId, StringComparison.Ordinal))
            {
                return;
            }

            CompleteInFlight(purchase.ProductId, MapFailure(purchase.ProductId, reason));
        }

        private PurchaseResult MapFailure(string productId, PurchaseFailureReason reason)
        {
            switch (reason)
            {
                case PurchaseFailureReason.UserCancelled:
                case PurchaseFailureReason.OrderCancelled:
                    return PurchaseResult.Cancelled(productId);
                case PurchaseFailureReason.PurchasingUnavailable:
                    return PurchaseResult.Unavailable(productId, "Purchases are disabled on this device.");
                case PurchaseFailureReason.ProductUnavailable:
                case PurchaseFailureReason.NotSupported:
                    return PurchaseResult.Unavailable(productId, "This item is not available right now.");
                case PurchaseFailureReason.StoreNotConnected:
                    return PurchaseResult.Unavailable(productId,
                        "The store is not available right now. Check your connection and try again.");
                case PurchaseFailureReason.PaymentDeclined:
                    return PurchaseResult.Failed(productId, "The payment was declined.");
                case PurchaseFailureReason.UserNotAuthenticated:
                    return PurchaseResult.Failed(productId, "Sign in to the store and try again.");
                case PurchaseFailureReason.ExistingPurchasePending:
                    return PurchaseResult.Pending(productId,
                        "A previous purchase is still being processed. It will be delivered automatically.");
                case PurchaseFailureReason.DuplicateTransaction:
                    IapProductConfig entry = _config.FindProduct(productId);
                    if (entry != null && !entry.IsConsumable && Owns(productId))
                    {
                        SyncOwned(productId, true);
                        return PurchaseResult.Succeeded(productId, "Already owned.");
                    }

                    // Owned but not yet consumed/acknowledged, or not in the purchase cache yet: re-read the
                    // purchases, which re-delivers it through OnPurchasePending / lists it as owned.
                    FetchPurchases();
                    return PurchaseResult.Pending(productId,
                        "This purchase is already being processed. It will be delivered automatically.");
                case PurchaseFailureReason.PurchaseMissing:
                    FetchPurchases();
                    return PurchaseResult.Failed(productId,
                        "The purchase could not be completed. If you were charged, the item will be delivered automatically.");
                default:
                    return PurchaseResult.Failed(productId, "The purchase could not be completed.");
            }
        }

        private void OnPurchaseDeferredEvent(DeferredOrder order)
        {
            Dispatch(() =>
            {
                string deferredId = FirstProductId(order);
                ServiceLog.Info("IAP: purchase of '" + deferredId + "' is awaiting approval/payment.");
                InFlightPurchase purchase = _inFlight;
                if (purchase != null && (string.IsNullOrEmpty(deferredId) || _config.FindProduct(deferredId) == null ||
                                         string.Equals(deferredId, purchase.ProductId, StringComparison.Ordinal)))
                {
                    CompleteInFlight(purchase.ProductId, PurchaseResult.Pending(purchase.ProductId,
                        "Your purchase is awaiting approval or payment. It will be delivered automatically once completed."));
                }

                RaiseAvailabilityChanged();
            }, "OnPurchaseDeferred");
        }

        private void CompleteInFlight(string productId, PurchaseResult result)
        {
            InFlightPurchase purchase = _inFlight;
            if (purchase == null || !string.Equals(purchase.ProductId, productId, StringComparison.Ordinal))
            {
                return;
            }

            _inFlight = null;
            CancelTimer(ref purchase.TimerId);
            Deliver(purchase.Callback, result);
        }

        // ------------------------------------------------------------------------------ confirmation

        private void Confirm(PendingOrder order)
        {
            string transactionId = TransactionIdOf(order);
            if (transactionId.Length == 0)
            {
                ServiceLog.Warn("IAP: '" + FirstProductId(order) + "' has no transaction id and cannot be confirmed.");
                return;
            }

            if (!_unconfirmed.TryGetValue(transactionId, out ConfirmEntry entry))
            {
                entry = new ConfirmEntry(order);
                _unconfirmed[transactionId] = entry;
            }
            else if (!entry.InFlight)
            {
                entry.Order = order;
            }

            // Not before the catalogue is loaded: an order fetched without it carries ProductType.Unknown, and Google
            // Play only consumes Consumable products (GoogleFinishTransactionUseCase) — a gem pack would be
            // acknowledged instead and stay owned (unbuyable) for the session. The entry is already granted and waits
            // here; HandlePurchasesFetched (after the products arrive) refreshes its order and retries.
            if (entry.InFlight || !_connected || _store == null || !_productsReady)
            {
                return; // retried when the store (re)connects / the catalogue arrives
            }

            entry.InFlight = true;
            try
            {
                // Result arrives through OnPurchaseConfirmed (possibly synchronously).
                _store.ConfirmPurchase(entry.Order);
            }
            catch (Exception e)
            {
                entry.InFlight = false;
                ServiceLog.Exception("IAP: ConfirmPurchase failed", e);
            }
        }

        private void OnPurchaseConfirmedEvent(Order order)
        {
            Dispatch(() => HandlePurchaseConfirmed(order), "OnPurchaseConfirmed");
        }

        private void HandlePurchaseConfirmed(Order order)
        {
            string transactionId = TransactionIdOf(order);
            ConfirmEntry entry = null;
            if (transactionId.Length > 0)
            {
                _unconfirmed.TryGetValue(transactionId, out entry);
            }

            if (order is ConfirmedOrder)
            {
                _unconfirmed.Remove(transactionId);
                ServiceLog.Info("IAP: purchase of '" + FirstProductId(order) + "' confirmed.");
                return;
            }

            if (!(order is FailedOrder failed))
            {
                return;
            }

            switch (failed.FailureReason)
            {
                case PurchaseFailureReason.DuplicateTransaction:
                    // Already consumed/acknowledged/finished (another device, a previous session…): nothing left to do.
                    _unconfirmed.Remove(transactionId);
                    ServiceLog.Info("IAP: purchase of '" + FirstProductId(order) + "' was already confirmed.");
                    return;
                case PurchaseFailureReason.ExistingPurchasePending:
                    return; // a confirmation for this transaction is already running
            }

            ServiceLog.Warn("IAP: confirming '" + FirstProductId(order) + "' failed: " + failed.FailureReason + " " +
                            failed.Details);
            if (entry == null)
            {
                return;
            }

            entry.InFlight = false;
            if (failed.FailureReason == PurchaseFailureReason.StoreNotConnected)
            {
                return; // retried on reconnect
            }

            entry.Failures++;
            if (entry.Failures < MaxConfirmAttempts)
            {
                if (_confirmRetryTimer == 0)
                {
                    _confirmRetryTimer = ServiceRunner.Delay(ConfirmRetrySeconds * entry.Failures, () =>
                    {
                        _confirmRetryTimer = 0;
                        RetryConfirmations();
                    });
                }
            }
            else
            {
                ServiceLog.Warn("IAP: giving up confirming '" + FirstProductId(order) + "' this session; the store " +
                                "re-delivers it on the next launch (already granted, so it is not granted again).");
            }
        }

        private void RetryConfirmations()
        {
            if (!_connected || _unconfirmed.Count == 0)
            {
                return;
            }

            var entries = new List<ConfirmEntry>(_unconfirmed.Values);
            for (int i = 0; i < entries.Count; i++)
            {
                ConfirmEntry entry = entries[i];
                if (!entry.InFlight && entry.Failures < MaxConfirmAttempts)
                {
                    Confirm(entry.Order);
                }
            }
        }

        // ------------------------------------------------------------------------------ existing purchases

        private void FetchPurchases()
        {
            if (!_connected || _purchasesFetching || _store == null)
            {
                return;
            }

            _purchasesFetching = true;
            CancelTimer(ref _purchasesFetchRetryTimer);
            CancelTimer(ref _purchasesFetchTimer);
            _purchasesFetchTimer = ServiceRunner.Delay(PurchasesFetchTimeoutSeconds, OnPurchasesFetchTimeout, true);
            try
            {
                // Unconfirmed purchases come back through OnPurchasePending, the rest through OnPurchasesFetched.
                _store.FetchPurchases();
            }
            catch (Exception e)
            {
                ServiceLog.Exception("IAP: FetchPurchases failed", e);
                HandlePurchasesFetchFailure("exception: " + e.Message);
            }
        }

        private void OnPurchasesFetchTimeout()
        {
            _purchasesFetchTimer = 0;
            _purchasesFetching = false;
        }

        private void OnPurchasesFetchedEvent(Orders orders)
        {
            Dispatch(() => HandlePurchasesFetched(orders), "OnPurchasesFetched");
        }

        private void HandlePurchasesFetched(Orders orders)
        {
            _purchasesFetching = false;
            _purchasesFetchRetries = 0;
            CancelTimer(ref _purchasesFetchTimer);
            bool restoring = _restoreAwaitingFetch;

            if (orders != null)
            {
                IReadOnlyList<PendingOrder> pending = orders.PendingOrders;
                if (pending != null)
                {
                    var toHandle = new List<PendingOrder>();
                    for (int i = 0; i < pending.Count; i++)
                    {
                        PendingOrder order = pending[i];
                        string transactionId = TransactionIdOf(order);
                        if (transactionId.Length > 0 && _unconfirmed.TryGetValue(transactionId, out ConfirmEntry entry))
                        {
                            if (!entry.InFlight)
                            {
                                entry.Order = order; // fresher object for the retry
                            }

                            continue;
                        }

                        // IAP normally routes these to OnPurchasePending itself (already handled / held then).
                        if (transactionId.Length > 0 &&
                            (_handledTransactions.Contains(transactionId) || IsHeld(transactionId)))
                        {
                            continue;
                        }

                        toHandle.Add(order);
                    }

                    for (int i = 0; i < toHandle.Count; i++)
                    {
                        HandlePendingOrder(toHandle[i]);
                    }
                }

                IReadOnlyList<ConfirmedOrder> confirmed = orders.ConfirmedOrders;
                if (confirmed != null)
                {
                    var owned = new List<string>();
                    for (int i = 0; i < confirmed.Count; i++)
                    {
                        CollectNonConsumables(confirmed[i], owned);
                    }

                    for (int i = 0; i < owned.Count; i++)
                    {
                        SyncOwned(owned[i], restoring);
                    }
                }
            }

            RetryConfirmations();
            RaiseAvailabilityChanged();
            if (restoring)
            {
                FinishRestore(true, null);
            }
        }

        private void OnPurchasesFetchFailedEvent(PurchasesFetchFailureDescription description)
        {
            string message = description != null ? description.FailureReason + " " + description.Message : "";
            Dispatch(() => HandlePurchasesFetchFailure(message), "OnPurchasesFetchFailed");
        }

        private void HandlePurchasesFetchFailure(string message)
        {
            _purchasesFetching = false;
            CancelTimer(ref _purchasesFetchTimer);
            ServiceLog.Warn("IAP: fetching purchases failed (" + message + ").");
            if (_restoreAwaitingFetch)
            {
                FinishRestore(false, message);
                return;
            }

            if (_connected && _purchasesFetchRetryTimer == 0 && _purchasesFetchRetries < MaxPurchasesFetchRetries)
            {
                _purchasesFetchRetries++;
                _purchasesFetchRetryTimer = ServiceRunner.Delay(PurchasesFetchRetrySeconds * _purchasesFetchRetries, () =>
                {
                    _purchasesFetchRetryTimer = 0;
                    FetchPurchases();
                });
            }
        }

        /// <summary>Announces a non-consumable the store reports as owned (once per session unless forced).</summary>
        private void SyncOwned(string productId, bool force)
        {
            _ownedThisSession.Add(productId);
            if (_entitlementGranted == null || (!force && _syncedNonConsumables.Contains(productId)))
            {
                return; // synced once a handler subscribes (Flush)
            }

            _syncedNonConsumables.Add(productId);
            ServiceLog.Info("IAP: store reports '" + productId + "' as owned.");
            RaiseEntitlementGranted(productId);
        }

        private void ScheduleFlush()
        {
            if (_flushScheduled)
            {
                return;
            }

            _flushScheduled = true;
            ServiceRunner.Post(Flush);
        }

        /// <summary>Processes held orders and syncs owned non-consumables once a handler is subscribed.</summary>
        private void Flush()
        {
            _flushScheduled = false;
            if (_entitlementGranted == null)
            {
                return;
            }

            if (_held.Count > 0)
            {
                PendingOrder[] held = _held.ToArray();
                _held.Clear();
                for (int i = 0; i < held.Length; i++)
                {
                    HandlePendingOrder(held[i]);
                }
            }

            if (_store == null)
            {
                return;
            }

            var owned = new List<string>();
            try
            {
                IReadOnlyList<Order> orders = _store.GetPurchases();
                for (int i = 0; orders != null && i < orders.Count; i++)
                {
                    if (orders[i] is ConfirmedOrder)
                    {
                        CollectNonConsumables(orders[i], owned);
                    }
                }
            }
            catch (Exception e)
            {
                ServiceLog.Warn("IAP: reading the purchase cache failed: " + e.Message);
            }

            for (int i = 0; i < owned.Count; i++)
            {
                SyncOwned(owned[i], false);
            }
        }

        // ------------------------------------------------------------------------------ restore

        /// <inheritdoc />
        public void Restore(Action<bool> onDone)
        {
            if (_store == null || !_connected)
            {
                Initialize();
                DeliverBool(onDone, false);
                return;
            }

            if (onDone != null)
            {
                _restoreCallbacks.Add(onDone);
            }

            if (_restoreRunning)
            {
                return;
            }

            _restoreRunning = true;
            _restoreAwaitingFetch = false;
            bool timeoutResult = IsAndroidPlayer; // Google Play restores automatically anyway
            _restoreTimer = ServiceRunner.Delay(RestoreTimeoutSeconds, () =>
            {
                _restoreTimer = 0;
                FinishRestore(timeoutResult, "timed out");
            }, true);
            ServiceLog.Info("IAP: restoring purchases.");

            if (!HasPlatformRestore())
            {
                // Editor fake store: nothing to ask the store; re-read the owned purchases.
                BeginRestoreFetchWait();
                FetchPurchases();
                return;
            }

            try
            {
                // On success IAP re-fetches the purchases (OnPurchasesFetched / OnPurchasePending) right before
                // calling back; the restore finishes once that list has been processed.
                _store.RestoreTransactions(OnRestoreTransactionsResult);
            }
            catch (Exception e)
            {
                ServiceLog.Exception("IAP: RestoreTransactions failed", e);
                FinishRestore(false, e.Message);
            }
        }

        private bool HasPlatformRestore()
        {
            try
            {
                return _store.AppleStoreExtendedPurchaseService != null ||
                       _store.GooglePlayStoreExtendedPurchaseService != null;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private void OnRestoreTransactionsResult(bool success, string error)
        {
            Dispatch(() =>
            {
                if (!_restoreRunning || _restoreAwaitingFetch)
                {
                    return;
                }

                if (success)
                {
                    BeginRestoreFetchWait();
                }
                else
                {
                    FinishRestore(false, error);
                }
            }, "RestoreTransactions callback");
        }

        private void BeginRestoreFetchWait()
        {
            _restoreAwaitingFetch = true;
            CancelTimer(ref _restoreFetchTimer);
            _restoreFetchTimer = ServiceRunner.Delay(RestoreFetchWaitSeconds, () =>
            {
                _restoreFetchTimer = 0;
                // The store accepted the restore; anything restored still arrives through EntitlementGranted.
                FinishRestore(true, "no purchase list received");
            }, true);
        }

        private void FinishRestore(bool success, string error)
        {
            if (!_restoreRunning)
            {
                return;
            }

            _restoreRunning = false;
            _restoreAwaitingFetch = false;
            CancelTimer(ref _restoreTimer);
            CancelTimer(ref _restoreFetchTimer);
            ServiceLog.Info("IAP: restore finished (" + (success ? "ok" : "failed") +
                            (string.IsNullOrEmpty(error) ? "" : ", " + error) + ").");

            Action<bool>[] callbacks = _restoreCallbacks.ToArray();
            _restoreCallbacks.Clear();
            RaiseAvailabilityChanged();
            for (int i = 0; i < callbacks.Length; i++)
            {
                DeliverBool(callbacks[i], success);
            }
        }

        // ------------------------------------------------------------------------------ order helpers

        private static string TransactionIdOf(Order order)
        {
            try
            {
                IOrderInfo info = order != null ? order.Info : null;
                string id = info != null ? info.TransactionID : null;
                return id ?? "";
            }
            catch (Exception)
            {
                return "";
            }
        }

        private static IReadOnlyList<CartItem> ItemsOf(Order order)
        {
            try
            {
                ICart cart = order != null ? order.CartOrdered : null;
                return cart != null ? cart.Items() : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string ProductIdOf(CartItem item)
        {
            Product product = item != null ? item.Product : null;
            if (product == null)
            {
                return null;
            }

            string id = product.uSku;
            if (string.IsNullOrEmpty(id) && product.definition != null)
            {
                id = product.definition.id;
            }

            return id;
        }

        private static string FirstProductId(Order order)
        {
            IReadOnlyList<CartItem> items = ItemsOf(order);
            return items != null && items.Count > 0 ? ProductIdOf(items[0]) : null;
        }

        private static bool ContainsProduct(Order order, string productId)
        {
            IReadOnlyList<CartItem> items = ItemsOf(order);
            for (int i = 0; items != null && i < items.Count; i++)
            {
                if (string.Equals(ProductIdOf(items[i]), productId, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Configured product ids in the order (distinct, cart order).</summary>
        private List<string> KnownProductIds(Order order)
        {
            var ids = new List<string>();
            IReadOnlyList<CartItem> items = ItemsOf(order);
            for (int i = 0; items != null && i < items.Count; i++)
            {
                string id = ProductIdOf(items[i]);
                if (!string.IsNullOrEmpty(id) && _config.FindProduct(id) != null && !ids.Contains(id))
                {
                    ids.Add(id);
                }
            }

            return ids;
        }

        private void CollectNonConsumables(Order order, List<string> into)
        {
            List<string> ids = KnownProductIds(order);
            for (int i = 0; i < ids.Count; i++)
            {
                IapProductConfig entry = _config.FindProduct(ids[i]);
                if (entry != null && !entry.IsConsumable && !into.Contains(ids[i]))
                {
                    into.Add(ids[i]);
                }
            }
        }

        // ------------------------------------------------------------------------------ helpers

        /// <summary>Runs an IAP event handler on the main thread (inline when already there), never throwing into IAP.</summary>
        private static void Dispatch(Action action, string what)
        {
            if (!ServiceRunner.IsMainThread)
            {
                ServiceRunner.Post(action);
                return;
            }

            try
            {
                action();
            }
            catch (Exception e)
            {
                ServiceLog.Exception("IAP: " + what + " handler failed", e);
            }
        }

        private static void ObserveTask(Task task)
        {
            if (task != null && !task.IsCompleted)
            {
                task.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
            }
        }

        private static void CancelTimer(ref int timerId)
        {
            if (timerId != 0)
            {
                ServiceRunner.Cancel(timerId);
                timerId = 0;
            }
        }

        private void RaiseAvailabilityChanged()
        {
            Action handler = AvailabilityChanged;
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
                ServiceLog.Exception("IAP: AvailabilityChanged handler failed", e);
            }
        }

        private void RaiseEntitlementGranted(string productId)
        {
            Action<string> handler = _entitlementGranted;
            if (handler == null)
            {
                return;
            }

            // Invoke subscribers one by one so a faulty UI listener cannot stop the one that grants.
            Delegate[] subscribers = handler.GetInvocationList();
            for (int i = 0; i < subscribers.Length; i++)
            {
                try
                {
                    ((Action<string>)subscribers[i])(productId);
                }
                catch (Exception e)
                {
                    ServiceLog.Exception("IAP: EntitlementGranted handler failed for '" + productId + "'", e);
                }
            }
        }

        private static void Deliver(Action<PurchaseResult> callback, PurchaseResult result)
        {
            if (callback == null)
            {
                return;
            }

            ServiceRunner.Post(() =>
            {
                try
                {
                    callback(result);
                }
                catch (Exception e)
                {
                    ServiceLog.Exception("IAP: purchase callback failed", e);
                }
            });
        }

        private static void DeliverBool(Action<bool> callback, bool value)
        {
            if (callback == null)
            {
                return;
            }

            ServiceRunner.Post(() =>
            {
                try
                {
                    callback(value);
                }
                catch (Exception e)
                {
                    ServiceLog.Exception("IAP: restore callback failed", e);
                }
            });
        }

        private sealed class InFlightPurchase
        {
            public readonly string ProductId;
            public readonly Action<PurchaseResult> Callback;
            public int TimerId;

            public InFlightPurchase(string productId, Action<PurchaseResult> callback)
            {
                ProductId = productId;
                Callback = callback;
            }
        }

        private sealed class ConfirmEntry
        {
            public PendingOrder Order;
            public bool InFlight;
            public int Failures;

            public ConfirmEntry(PendingOrder order)
            {
                Order = order;
            }
        }
    }
}

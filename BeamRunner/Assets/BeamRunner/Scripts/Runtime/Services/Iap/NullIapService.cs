using System;

namespace BeamRunner.Services
{
    /// <summary>
    /// IAP service for platforms without a supported store (desktop, WebGL) or an empty catalogue. Never
    /// available; prices fall back to the configured labels; purchases report
    /// <see cref="PurchaseStatus.Unavailable"/> on the next frame.
    /// </summary>
    public sealed class NullIapService : IIapService
    {
        private readonly MonetizationConfig _config;

        /// <summary>Creates the null service.</summary>
        public NullIapService(MonetizationConfig config)
        {
            _config = config ?? MonetizationConfig.Current;
        }

        /// <inheritdoc />
        public event Action AvailabilityChanged
        {
            add { }
            remove { }
        }

        /// <inheritdoc />
        public event Action<string> EntitlementGranted
        {
            add { }
            remove { }
        }

        /// <inheritdoc />
        public bool Available => false;

        /// <inheritdoc />
        public bool Initializing => false;

        /// <inheritdoc />
        public bool PurchaseInProgress => false;

        /// <inheritdoc />
        public bool SupportsRestore => false;

        /// <inheritdoc />
        public void Initialize()
        {
        }

        /// <inheritdoc />
        public string GetPrice(string productId)
        {
            IapProductConfig entry = _config.FindProduct(productId);
            return entry != null ? entry.FallbackPrice : "";
        }

        /// <inheritdoc />
        public bool Owns(string productId)
        {
            return false;
        }

        /// <inheritdoc />
        public void Purchase(string productId, Action<PurchaseResult> onDone)
        {
            if (onDone == null)
            {
                return;
            }

            PurchaseResult result = PurchaseResult.Unavailable(productId,
                "In-app purchases are not available on this platform.");
            ServiceRunner.Post(() =>
            {
                try
                {
                    onDone(result);
                }
                catch (Exception e)
                {
                    ServiceLog.Exception("IAP: purchase callback failed", e);
                }
            });
        }

        /// <inheritdoc />
        public void Restore(Action<bool> onDone)
        {
            if (onDone == null)
            {
                return;
            }

            ServiceRunner.Post(() =>
            {
                try
                {
                    onDone(false);
                }
                catch (Exception e)
                {
                    ServiceLog.Exception("IAP: restore callback failed", e);
                }
            });
        }
    }
}

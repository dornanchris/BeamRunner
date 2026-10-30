namespace BeamRunner.Services
{
    /// <summary>Outcome category of a purchase request.</summary>
    public enum PurchaseStatus
    {
        /// <summary>Paid and delivered (<see cref="IIapService.EntitlementGranted"/> has already fired).</summary>
        Success,

        /// <summary>The player backed out. Show nothing or a subtle note.</summary>
        Cancelled,

        /// <summary>The store rejected or could not complete the purchase; see the message.</summary>
        Failed,

        /// <summary>The store is not reachable/configured, or the product does not exist in it.</summary>
        Unavailable,

        /// <summary>
        /// Awaiting approval or payment (Apple "Ask to Buy", Google Play pending payment methods). The item is
        /// delivered later through <see cref="IIapService.EntitlementGranted"/>, possibly in a later session.
        /// </summary>
        Pending,
    }

    /// <summary>Result passed to the <see cref="IIapService.Purchase"/> callback.</summary>
    public readonly struct PurchaseResult
    {
        /// <summary>Creates a result.</summary>
        public PurchaseResult(PurchaseStatus status, string productId, string message)
        {
            Status = status;
            ProductId = productId ?? "";
            Message = message ?? "";
        }

        /// <summary>Outcome category.</summary>
        public PurchaseStatus Status { get; }

        /// <summary>The product that was requested.</summary>
        public string ProductId { get; }

        /// <summary>Short, player-presentable English explanation (may be empty for success/cancel).</summary>
        public string Message { get; }

        /// <summary>True when <see cref="Status"/> is <see cref="PurchaseStatus.Success"/>.</summary>
        public bool IsSuccess => Status == PurchaseStatus.Success;

        /// <summary>Convenience factory.</summary>
        public static PurchaseResult Succeeded(string productId, string message = null)
        {
            return new PurchaseResult(PurchaseStatus.Success, productId, message);
        }

        /// <summary>Convenience factory.</summary>
        public static PurchaseResult Cancelled(string productId)
        {
            return new PurchaseResult(PurchaseStatus.Cancelled, productId, "");
        }

        /// <summary>Convenience factory.</summary>
        public static PurchaseResult Failed(string productId, string message)
        {
            return new PurchaseResult(PurchaseStatus.Failed, productId, message);
        }

        /// <summary>Convenience factory.</summary>
        public static PurchaseResult Unavailable(string productId, string message)
        {
            return new PurchaseResult(PurchaseStatus.Unavailable, productId, message);
        }

        /// <summary>Convenience factory.</summary>
        public static PurchaseResult Pending(string productId, string message)
        {
            return new PurchaseResult(PurchaseStatus.Pending, productId, message);
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return Status + " (" + ProductId + ")" + (string.IsNullOrEmpty(Message) ? "" : ": " + Message);
        }
    }
}

using HashKeyChain.Domain;
using HashKeyChain.Localization;

namespace HashKeyChain.Services.Trades;

/// <summary>Input for registering a new trade (spec §5).</summary>
public sealed class TradeConditionsInput
{
    public int BuyerCompanyId { get; set; }
    public int SellerCompanyId { get; set; }
    public string BuyerWalletAddress { get; set; } = string.Empty;
    public string SellerWalletAddress { get; set; } = string.Empty;
    public TransportMode TransportMode { get; set; } = TransportMode.Sea;
    public string PaymentToken { get; set; } = "MockUSDC";
    public decimal PaymentAmount { get; set; }
    public string Currency { get; set; } = "USDC";
    public string? PurchaseOrderNumber { get; set; }
    public string? ExpectedProductDescription { get; set; }
    public decimal? ExpectedQuantity { get; set; }
    public DateTime? LatestShipmentDate { get; set; }
    public DateTime? DocumentSubmissionDeadline { get; set; }
    public DateTime? PaymentExpiry { get; set; }
    public string? Notes { get; set; }
}

/// <summary>Thrown when a trade operation is attempted in an incompatible state or without permission.</summary>
public sealed class TradeOperationException(string message) : InvalidOperationException(message);

/// <summary>
/// Trade registration and condition-approval lifecycle (spec §5/§6). Enforces:
/// mandatory fields, the "seller wallet is authoritative and never silently
/// overwritten" rule, and condition locking after approval.
/// </summary>
public interface ITradeService
{
    Task<Trade> CreateDraftAsync(TradeConditionsInput input, string actor, CancellationToken ct = default);
    Task<Trade> UpdateConditionsAsync(int tradeId, TradeConditionsInput input, string actor, CancellationToken ct = default);
    Task<Trade> RequestApprovalAsync(int tradeId, string actor, CancellationToken ct = default);
    Task<Trade> ApproveConditionsAsync(int tradeId, string actor, string? comment = null, CancellationToken ct = default);
    Task<Trade> CancelAsync(int tradeId, string actor, string? reason = null, CancellationToken ct = default);
}

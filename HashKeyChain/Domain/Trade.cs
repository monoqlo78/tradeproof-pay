using System.ComponentModel.DataAnnotations;
using HashKeyChain.Localization;

namespace HashKeyChain.Domain;

/// <summary>
/// A trade — the central aggregate. Holds the registered trade conditions
/// (spec §5), the current lifecycle <see cref="TradeStatus"/>, and pointers to
/// documents, checks, audit entries and chain transactions.
/// State is only ever changed through the state-machine service.
/// </summary>
public sealed class Trade
{
    public int Id { get; set; }

    // ---- Identity / parties (spec §5) ----
    [MaxLength(64)]
    public string TradeReference { get; set; } = string.Empty;

    public int BuyerCompanyId { get; set; }
    public Company? BuyerCompany { get; set; }

    public int SellerCompanyId { get; set; }
    public Company? SellerCompany { get; set; }

    /// <summary>Authoritative buyer wallet (funding + refund target).</summary>
    [MaxLength(64)]
    public string BuyerWalletAddress { get; set; } = string.Empty;

    /// <summary>Authoritative seller wallet (payout target). Never overwritten from an invoice (§5).</summary>
    [MaxLength(64)]
    public string SellerWalletAddress { get; set; } = string.Empty;

    // ---- Shipment / payment terms ----
    public TransportMode TransportMode { get; set; }

    [MaxLength(32)]
    public string PaymentToken { get; set; } = "MockUSDC";

    public decimal PaymentAmount { get; set; }

    [MaxLength(16)]
    public string Currency { get; set; } = "USDC";

    [MaxLength(64)]
    public string? PurchaseOrderNumber { get; set; }

    [MaxLength(1024)]
    public string? ExpectedProductDescription { get; set; }

    public decimal? ExpectedQuantity { get; set; }

    public DateTime? LatestShipmentDate { get; set; }
    public DateTime? DocumentSubmissionDeadline { get; set; }
    public DateTime? PaymentExpiry { get; set; }

    // ---- People ----
    public int? VerifierUserId { get; set; }
    public AppUser? VerifierUser { get; set; }

    public int? BuyerApproverUserId { get; set; }
    public AppUser? BuyerApproverUser { get; set; }

    [MaxLength(2048)]
    public string? Notes { get; set; }

    // ---- Lifecycle ----
    public TradeStatus Status { get; set; } = TradeStatus.Draft;

    /// <summary>
    /// Set true once trade conditions are approved. Critical fields (seller,
    /// seller wallet, token, amount, required docs, latest shipment date,
    /// payment expiry) may not be freely edited afterwards (spec §6).
    /// </summary>
    public bool ConditionsLocked { get; set; }

    public VerificationResult? LatestVerdict { get; set; }

    // ---- Escrow / settlement snapshot ----
    public bool IsFunded { get; set; }
    public bool IsSettled { get; set; }
    public bool IsRefunded { get; set; }

    [MaxLength(66)]
    public string? SettlementTxHash { get; set; }

    [MaxLength(66)]
    public string? RefundTxHash { get; set; }

    // ---- Audit timestamps ----
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    [MaxLength(128)]
    public string? CreatedBy { get; set; }

    // ---- Navigation ----
    public ICollection<TradeDocument> Documents { get; set; } = new List<TradeDocument>();
    public ICollection<VerificationRun> VerificationRuns { get; set; } = new List<VerificationRun>();
    public ICollection<AuditEntry> AuditEntries { get; set; } = new List<AuditEntry>();
    public ICollection<ChainTransaction> ChainTransactions { get; set; } = new List<ChainTransaction>();
}

using System.ComponentModel.DataAnnotations;

namespace HashKeyChain.Domain;

/// <summary>
/// Record of an on-chain (or mock) transaction related to a trade (chain doc §4).
/// Stores the fields required for display and audit.
/// </summary>
public sealed class ChainTransaction
{
    public int Id { get; set; }

    public int TradeId { get; set; }
    public Trade? Trade { get; set; }

    /// <summary>Logical operation, e.g. "Fund", "RegisterHashes", "Release", "Refund".</summary>
    [MaxLength(32)]
    public string Operation { get; set; } = string.Empty;

    [MaxLength(66)]
    public string? TransactionHash { get; set; }

    public long? BlockNumber { get; set; }

    [MaxLength(64)]
    public string? FromAddress { get; set; }

    [MaxLength(64)]
    public string? ToAddress { get; set; }

    [MaxLength(64)]
    public string? ContractAddress { get; set; }

    public int ChainId { get; set; }

    public long? GasUsed { get; set; }

    /// <summary>Receipt status: Pending / Success / Failed.</summary>
    [MaxLength(16)]
    public string Status { get; set; } = "Pending";

    public DateTime TimestampUtc { get; set; }

    [MaxLength(512)]
    public string? ExplorerUrl { get; set; }
}

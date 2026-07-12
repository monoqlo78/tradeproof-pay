using System.ComponentModel.DataAnnotations;
using HashKeyChain.Localization;

namespace HashKeyChain.Domain;

/// <summary>
/// Append-only audit entry (spec §21). Regular users cannot edit or delete these.
/// </summary>
public sealed class AuditEntry
{
    public long Id { get; set; }

    public int? TradeId { get; set; }
    public Trade? Trade { get; set; }

    public AuditAction Action { get; set; }

    [MaxLength(128)]
    public string Actor { get; set; } = string.Empty;

    public DateTime TimestampUtc { get; set; }

    public TradeStatus? BeforeStatus { get; set; }
    public TradeStatus? AfterStatus { get; set; }

    /// <summary>Correlation id linking related operations (e.g. a settlement flow).</summary>
    [MaxLength(64)]
    public string? CorrelationId { get; set; }

    [MaxLength(66)]
    public string? TransactionHash { get; set; }

    [MaxLength(2048)]
    public string? Comment { get; set; }
}

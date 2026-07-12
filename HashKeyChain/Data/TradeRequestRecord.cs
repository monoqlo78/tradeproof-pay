using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HashKeyChain.Data;

/// <summary>
/// Persisted record of a submitted trade-demo request. Stored in the dedicated
/// <c>hashkeychain</c> schema so it never collides with other tables in the
/// shared database.
/// </summary>
[Table("TradeRequests", Schema = AppDbContext.Schema)]
public sealed class TradeRequestRecord
{
    public int Id { get; set; }

    [MaxLength(32)]
    public string TradeReference { get; set; } = string.Empty;

    [Column(TypeName = "decimal(18,2)")]
    public decimal Amount { get; set; }

    [MaxLength(256)]
    public string Email { get; set; } = string.Empty;

    [MaxLength(16)]
    public string UiCulture { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }
}

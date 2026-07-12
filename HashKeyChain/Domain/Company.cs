using System.ComponentModel.DataAnnotations;

namespace HashKeyChain.Domain;

/// <summary>
/// A buyer or seller company participating in trades.
/// </summary>
public sealed class Company
{
    public int Id { get; set; }

    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The company's registered wallet address. This is the authoritative payout
    /// target for a seller and must never be auto-overwritten from an uploaded
    /// invoice (spec §5).
    /// </summary>
    [MaxLength(64)]
    public string? WalletAddress { get; set; }

    [MaxLength(8)]
    public string? CountryCode { get; set; }

    public ICollection<AppUser> Users { get; set; } = new List<AppUser>();
}

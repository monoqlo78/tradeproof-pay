using System.ComponentModel.DataAnnotations;

namespace HashKeyChain.Domain;

/// <summary>
/// A demo application user. A user may hold multiple <see cref="TradeRole"/>s
/// (stored via <see cref="UserRole"/>); the active role is shown in the UI.
/// </summary>
public sealed class AppUser
{
    public int Id { get; set; }

    [MaxLength(128)]
    public string DisplayName { get; set; } = string.Empty;

    [MaxLength(256)]
    public string Email { get; set; } = string.Empty;

    public int? CompanyId { get; set; }
    public Company? Company { get; set; }

    [MaxLength(16)]
    public string PreferredCulture { get; set; } = "ja-JP";

    public ICollection<UserRole> Roles { get; set; } = new List<UserRole>();
}

/// <summary>
/// Join entity assigning a <see cref="TradeRole"/> to an <see cref="AppUser"/>.
/// </summary>
public sealed class UserRole
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public AppUser? User { get; set; }

    public TradeRole Role { get; set; }
}

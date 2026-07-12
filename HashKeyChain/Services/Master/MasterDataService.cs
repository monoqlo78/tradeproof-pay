using HashKeyChain.Data;
using HashKeyChain.Domain;
using Microsoft.EntityFrameworkCore;

namespace HashKeyChain.Services.Master;

/// <summary>Thrown when a master-data operation is invalid (duplicate, or a
/// delete that would break referential integrity of existing trades).</summary>
public sealed class MasterDataException(string message) : InvalidOperationException(message);

/// <summary>
/// CRUD for the master entities (companies and users) behind the /master screens.
/// All work stays in the <c>hashkeychain</c> schema and is strictly additive /
/// referential-safe: deletes are refused when a row is still referenced by a
/// trade, so existing business data can never be orphaned or lost (spec data
/// safety). Administrator-only in the UI.
/// </summary>
public interface IMasterDataService
{
    // Companies
    Task<IReadOnlyList<Company>> GetCompaniesAsync(CancellationToken ct = default);
    Task<Company?> GetCompanyAsync(int id, CancellationToken ct = default);
    Task<Company> CreateCompanyAsync(Company input, CancellationToken ct = default);
    Task<Company> UpdateCompanyAsync(int id, Company input, CancellationToken ct = default);
    Task DeleteCompanyAsync(int id, CancellationToken ct = default);

    // Users
    Task<IReadOnlyList<AppUser>> GetUsersAsync(CancellationToken ct = default);
    Task<AppUser?> GetUserAsync(int id, CancellationToken ct = default);
    Task<AppUser> CreateUserAsync(AppUser input, IEnumerable<TradeRole> roles, CancellationToken ct = default);
    Task<AppUser> UpdateUserAsync(int id, AppUser input, IEnumerable<TradeRole> roles, CancellationToken ct = default);
    Task DeleteUserAsync(int id, CancellationToken ct = default);
}

public sealed class MasterDataService(IDbContextFactory<AppDbContext> factory) : IMasterDataService
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    // ---- Companies ----

    public async Task<IReadOnlyList<Company>> GetCompaniesAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Companies.Include(c => c.Users).AsNoTracking().OrderBy(c => c.Id).ToListAsync(ct);
    }

    public async Task<Company?> GetCompanyAsync(int id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Companies.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
    }

    public async Task<Company> CreateCompanyAsync(Company input, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var name = (input.Name ?? string.Empty).Trim();
        if (name.Length == 0)
            throw new MasterDataException("Company name is required.");

        await using var db = await _factory.CreateDbContextAsync(ct);
        if (await db.Companies.AnyAsync(c => c.Name == name, ct))
            throw new MasterDataException($"A company named '{name}' already exists.");

        var company = new Company
        {
            Name = name,
            WalletAddress = Normalize(input.WalletAddress),
            CountryCode = Normalize(input.CountryCode)
        };
        db.Companies.Add(company);
        await db.SaveChangesAsync(ct);
        return company;
    }

    public async Task<Company> UpdateCompanyAsync(int id, Company input, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var name = (input.Name ?? string.Empty).Trim();
        if (name.Length == 0)
            throw new MasterDataException("Company name is required.");

        await using var db = await _factory.CreateDbContextAsync(ct);
        var company = await db.Companies.FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new MasterDataException($"Company {id} was not found.");

        if (await db.Companies.AnyAsync(c => c.Name == name && c.Id != id, ct))
            throw new MasterDataException($"A company named '{name}' already exists.");

        company.Name = name;
        company.WalletAddress = Normalize(input.WalletAddress);
        company.CountryCode = Normalize(input.CountryCode);
        await db.SaveChangesAsync(ct);
        return company;
    }

    public async Task DeleteCompanyAsync(int id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var company = await db.Companies.FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new MasterDataException($"Company {id} was not found.");

        if (await db.Trades.AnyAsync(t => t.BuyerCompanyId == id || t.SellerCompanyId == id, ct))
            throw new MasterDataException("This company is referenced by existing trades and cannot be deleted.");
        if (await db.Users.AnyAsync(u => u.CompanyId == id, ct))
            throw new MasterDataException("This company still has users. Reassign or delete them first.");

        db.Companies.Remove(company);
        await db.SaveChangesAsync(ct);
    }

    // ---- Users ----

    public async Task<IReadOnlyList<AppUser>> GetUsersAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Users
            .Include(u => u.Company)
            .Include(u => u.Roles)
            .AsNoTracking()
            .OrderBy(u => u.Id)
            .ToListAsync(ct);
    }

    public async Task<AppUser?> GetUserAsync(int id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Users
            .Include(u => u.Company)
            .Include(u => u.Roles)
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == id, ct);
    }

    public async Task<AppUser> CreateUserAsync(AppUser input, IEnumerable<TradeRole> roles, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var (name, email) = ValidateUser(input);

        await using var db = await _factory.CreateDbContextAsync(ct);
        if (await db.Users.AnyAsync(u => u.Email == email, ct))
            throw new MasterDataException($"A user with email '{email}' already exists.");
        await ValidateCompanyAsync(db, input.CompanyId, ct);

        var user = new AppUser
        {
            DisplayName = name,
            Email = email,
            CompanyId = input.CompanyId,
            PreferredCulture = NormalizeCulture(input.PreferredCulture)
        };
        foreach (var role in roles.Distinct())
            user.Roles.Add(new UserRole { Role = role });

        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        return user;
    }

    public async Task<AppUser> UpdateUserAsync(int id, AppUser input, IEnumerable<TradeRole> roles, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var (name, email) = ValidateUser(input);

        await using var db = await _factory.CreateDbContextAsync(ct);
        var user = await db.Users.Include(u => u.Roles).FirstOrDefaultAsync(u => u.Id == id, ct)
            ?? throw new MasterDataException($"User {id} was not found.");

        if (await db.Users.AnyAsync(u => u.Email == email && u.Id != id, ct))
            throw new MasterDataException($"A user with email '{email}' already exists.");
        await ValidateCompanyAsync(db, input.CompanyId, ct);

        user.DisplayName = name;
        user.Email = email;
        user.CompanyId = input.CompanyId;
        user.PreferredCulture = NormalizeCulture(input.PreferredCulture);

        var desired = roles.Distinct().ToHashSet();
        db.UserRoles.RemoveRange(user.Roles.Where(r => !desired.Contains(r.Role)));
        foreach (var role in desired.Where(r => user.Roles.All(x => x.Role != r)))
            user.Roles.Add(new UserRole { Role = role });

        await db.SaveChangesAsync(ct);
        return user;
    }

    public async Task DeleteUserAsync(int id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct)
            ?? throw new MasterDataException($"User {id} was not found.");

        if (await db.Trades.AnyAsync(t => t.VerifierUserId == id || t.BuyerApproverUserId == id, ct))
            throw new MasterDataException("This user is referenced by existing trades and cannot be deleted.");

        db.Users.Remove(user);
        await db.SaveChangesAsync(ct);
    }

    // ---- Helpers ----

    private static (string Name, string Email) ValidateUser(AppUser input)
    {
        var name = (input.DisplayName ?? string.Empty).Trim();
        var email = (input.Email ?? string.Empty).Trim();
        if (name.Length == 0)
            throw new MasterDataException("Display name is required.");
        if (email.Length == 0 || !email.Contains('@'))
            throw new MasterDataException("A valid email is required.");
        return (name, email);
    }

    private static async Task ValidateCompanyAsync(AppDbContext db, int? companyId, CancellationToken ct)
    {
        if (companyId is { } cid && !await db.Companies.AnyAsync(c => c.Id == cid, ct))
            throw new MasterDataException($"Company {cid} was not found.");
    }

    private static string? Normalize(string? s)
    {
        var t = s?.Trim();
        return string.IsNullOrEmpty(t) ? null : t;
    }

    private static string NormalizeCulture(string? culture) =>
        culture is "en-US" or "ja-JP" ? culture : "ja-JP";
}

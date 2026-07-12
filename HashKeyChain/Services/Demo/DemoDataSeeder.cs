using HashKeyChain.Data;
using HashKeyChain.Domain;
using Microsoft.EntityFrameworkCore;

namespace HashKeyChain.Services.Demo;

/// <summary>
/// Seeds deterministic demo companies and users (one per role) so the DemoMode
/// UI has real parties to select and the separation of duties can be exercised
/// (spec §4/§25). Idempotent: it only inserts rows that do not already exist and
/// never modifies existing data. Runs only in DemoMode against a real database.
/// </summary>
public interface IDemoDataSeeder
{
    Task SeedAsync(CancellationToken ct = default);
}

public sealed class DemoDataSeeder(IDbContextFactory<AppDbContext> factory, ILogger<DemoDataSeeder> logger) : IDemoDataSeeder
{
    public const string BuyerCompanyName = "TradeProof Buyer K.K.";
    public const string SellerCompanyName = "Horizon Seller Co., Ltd.";

    private readonly IDbContextFactory<AppDbContext> _factory = factory;
    private readonly ILogger<DemoDataSeeder> _logger = logger;

    public async Task SeedAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        var buyer = await db.Companies.FirstOrDefaultAsync(c => c.Name == BuyerCompanyName, ct);
        if (buyer is null)
        {
            buyer = new Company
            {
                Name = BuyerCompanyName,
                CountryCode = "JP",
                WalletAddress = "0xB0B0000000000000000000000000000000000001"
            };
            db.Companies.Add(buyer);
        }

        var seller = await db.Companies.FirstOrDefaultAsync(c => c.Name == SellerCompanyName, ct);
        if (seller is null)
        {
            seller = new Company
            {
                Name = SellerCompanyName,
                CountryCode = "VN",
                WalletAddress = "0x5E11E20000000000000000000000000000000002"
            };
            db.Companies.Add(seller);
        }

        await db.SaveChangesAsync(ct);

        var demoUsers = new (string Email, string Name, int CompanyId, TradeRole[] Roles)[]
        {
            ("operator@tradeproof.demo", "Buyer Operator", buyer.Id, new[] { TradeRole.BuyerOperator }),
            ("approver@tradeproof.demo", "Buyer Approver", buyer.Id, new[] { TradeRole.BuyerApprover }),
            ("seller@tradeproof.demo", "Seller User", seller.Id, new[] { TradeRole.Seller }),
            ("verifier@tradeproof.demo", "Trade Verifier", buyer.Id, new[] { TradeRole.TradeVerifier }),
            ("admin@tradeproof.demo", "Administrator", buyer.Id,
                new[] { TradeRole.Administrator, TradeRole.BuyerOperator, TradeRole.BuyerApprover, TradeRole.TradeVerifier })
        };

        foreach (var (email, name, companyId, roles) in demoUsers)
        {
            if (await db.Users.AnyAsync(u => u.Email == email, ct))
                continue;

            var user = new AppUser { Email = email, DisplayName = name, CompanyId = companyId, PreferredCulture = "ja-JP" };
            foreach (var role in roles)
                user.Roles.Add(new UserRole { Role = role });
            db.Users.Add(user);
        }

        await db.SaveChangesAsync(ct);
        _logger.LogInformation("Demo data seed complete ({Companies} companies, {Users} demo users).",
            await db.Companies.CountAsync(ct), await db.Users.CountAsync(ct));
    }
}

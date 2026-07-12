using HashKeyChain.Configuration;
using HashKeyChain.Domain;
using HashKeyChain.Services.Demo;

namespace HashKeyChain.Services.Settings;

/// <summary>
/// Resolves the effective "own" (buyer / importer) company from the saved settings,
/// falling back to the seeded JP buyer company and finally to the first company, so
/// the app always has a sensible self even before an admin configures it.
/// </summary>
public static class OrgSettingsResolver
{
    public static Company? ResolveOwnCompany(OrgSettings settings, IReadOnlyList<Company> companies)
    {
        if (companies.Count == 0)
            return null;

        if (settings.OwnCompanyId is { } id)
        {
            var match = companies.FirstOrDefault(c => c.Id == id);
            if (match is not null)
                return match;
        }

        return companies.FirstOrDefault(c => c.Name == DemoDataSeeder.BuyerCompanyName)
               ?? companies[0];
    }

    /// <summary>The wallet to use for the own company: the settings override, then the company's own wallet.</summary>
    public static string? ResolveOwnWallet(OrgSettings settings, Company? ownCompany) =>
        !string.IsNullOrWhiteSpace(settings.OwnWalletAddress)
            ? settings.OwnWalletAddress
            : ownCompany?.WalletAddress;
}

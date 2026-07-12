using Microsoft.AspNetCore.Localization;

namespace HashKeyChain.Localization;

/// <summary>
/// Highest-priority culture provider: uses the signed-in user's stored
/// PreferredCulture (exposed as a claim) when present and supported. Falls
/// through to the cookie / Accept-Language providers otherwise.
/// </summary>
public sealed class UserProfileRequestCultureProvider : RequestCultureProvider
{
    public const string PreferredCultureClaimType = "preferred_culture";

    public override Task<ProviderCultureResult?> DetermineProviderCultureResult(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var user = httpContext.User;
        if (user.Identity?.IsAuthenticated == true)
        {
            var culture = user.FindFirst(PreferredCultureClaimType)?.Value;
            if (LocalizationConstants.IsSupported(culture))
            {
                return Task.FromResult<ProviderCultureResult?>(
                    new ProviderCultureResult(culture, culture));
            }
        }

        return NullProviderCultureResult;
    }
}

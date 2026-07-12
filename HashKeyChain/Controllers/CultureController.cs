using HashKeyChain.Localization;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;

namespace HashKeyChain.Controllers;

/// <summary>
/// Handles switching the UI language. Persists the choice in the culture cookie
/// for ~1 year and returns to the originating page, guarding against open
/// redirects.
/// </summary>
[Route("[controller]/[action]")]
public sealed class CultureController : Controller
{
    [HttpGet]
    public IActionResult Set(string culture, string? redirectUri)
    {
        // Fall back to the default culture when an unsupported value is supplied.
        var target = LocalizationConstants.IsSupported(culture)
            ? culture
            : LocalizationConstants.DefaultCulture;

        Response.Cookies.Append(
            CookieRequestCultureProvider.DefaultCookieName,
            CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(target)),
            new CookieOptions
            {
                Expires = DateTimeOffset.UtcNow.AddYears(1),
                IsEssential = true,
                HttpOnly = false,
                SameSite = SameSiteMode.Lax
            });

        // Prevent open redirects: only ever return to a local URL.
        if (string.IsNullOrEmpty(redirectUri) || !Url.IsLocalUrl(redirectUri))
        {
            redirectUri = "/";
        }

        return LocalRedirect(redirectUri);
    }
}

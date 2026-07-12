using HashKeyChain.Localization;
using Microsoft.AspNetCore.Mvc;

namespace HashKeyChain.Controllers;

/// <summary>
/// Returns the localized client-side UI strings as JSON for the current culture.
/// The response keys are stable English identifiers; only the values are
/// translated. This is the safe alternative to embedding a window.* object.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public sealed class ClientResourcesController : ControllerBase
{
    private readonly IClientLocalizationProvider _provider;

    public ClientResourcesController(IClientLocalizationProvider provider) => _provider = provider;

    [HttpGet]
    public IReadOnlyDictionary<string, string> Get() => _provider.GetResources();
}

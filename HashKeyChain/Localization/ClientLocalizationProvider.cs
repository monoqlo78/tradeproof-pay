using System.Text.Json;
using Microsoft.Extensions.Localization;

namespace HashKeyChain.Localization;

/// <summary>
/// Provides the set of UI strings that the browser / TypeScript layer needs,
/// already translated for the current UI culture. The JSON keys are stable,
/// English identifiers (e.g. <c>walletConnected</c>) so client code never
/// contains hard-coded Japanese or English text.
/// </summary>
public interface IClientLocalizationProvider
{
    IReadOnlyDictionary<string, string> GetResources();

    string GetResourcesJson();
}

/// <inheritdoc />
public sealed class ClientLocalizationProvider : IClientLocalizationProvider
{
    // Client key -> shared resource key. Keys mirror the TypeScript contract
    // (window.hashKeyChainResources) and must not be translated.
    private static readonly IReadOnlyDictionary<string, string> KeyMap =
        new Dictionary<string, string>
        {
            ["walletConnected"] = "Client_walletConnected",
            ["walletNotFound"] = "Client_walletNotFound",
            ["wrongNetwork"] = "Client_wrongNetwork",
            ["transactionPending"] = "Client_transactionPending",
            ["transactionConfirmed"] = "Client_transactionConfirmed",
            ["transactionFailed"] = "Client_transactionFailed",
            ["duplicateSettlement"] = "Client_duplicateSettlement",
            ["confirmationRequired"] = "Client_confirmationRequired",
        };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };

    private readonly IStringLocalizer<SharedResource> _localizer;

    public ClientLocalizationProvider(IStringLocalizer<SharedResource> localizer) => _localizer = localizer;

    public IReadOnlyDictionary<string, string> GetResources() =>
        KeyMap.ToDictionary(kvp => kvp.Key, kvp => _localizer[kvp.Value].Value);

    public string GetResourcesJson() =>
        JsonSerializer.Serialize(GetResources(), JsonOptions);
}

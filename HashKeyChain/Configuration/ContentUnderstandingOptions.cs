namespace HashKeyChain.Configuration;

/// <summary>
/// Azure AI Content Understanding configuration. Used to extract structured trade
/// fields from an uploaded contract (and other trade documents) so the trade
/// creation form can be auto-filled, and to cross-check received shipping
/// documents. When <see cref="Enabled"/> is false or the key/endpoint is missing,
/// the app degrades gracefully: the contract auto-fill is simply unavailable and
/// document analysis falls back to Document Intelligence / the deterministic mock,
/// so DemoMode always works.
/// </summary>
public sealed class ContentUnderstandingOptions
{
    public const string SectionName = "ContentUnderstanding";

    public bool Enabled { get; set; }

    /// <summary>Resource endpoint, e.g. https://name.cognitiveservices.azure.com.</summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>API key. Provided via user-secrets / Key Vault / app settings, not appsettings.json.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Custom analyzer id trained for trade documents.</summary>
    public string AnalyzerId { get; set; } = "tradeproof_tradedoc";

    public string ApiVersion { get; set; } = "2025-11-01";

    public bool IsConfigured =>
        Enabled
        && !string.IsNullOrWhiteSpace(Endpoint)
        && !string.IsNullOrWhiteSpace(ApiKey)
        && !string.IsNullOrWhiteSpace(AnalyzerId);
}

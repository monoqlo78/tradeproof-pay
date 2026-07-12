namespace HashKeyChain.Configuration;

/// <summary>
/// Azure AI Document Intelligence configuration for real OCR of received trade
/// documents (bill of lading, commercial invoice, packing list, ...). When
/// <see cref="Enabled"/> is false or the key is missing, the app falls back to
/// the deterministic mock analyzer so DemoMode always works.
/// </summary>
public sealed class DocumentIntelligenceOptions
{
    public const string SectionName = "DocumentIntelligence";

    public bool Enabled { get; set; }

    /// <summary>Resource endpoint, e.g. https://name.cognitiveservices.azure.com.</summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>API key. Provided via user-secrets / Key Vault, not appsettings.</summary>
    public string ApiKey { get; set; } = string.Empty;

    public bool IsConfigured =>
        Enabled
        && !string.IsNullOrWhiteSpace(Endpoint)
        && !string.IsNullOrWhiteSpace(ApiKey);
}

namespace HashKeyChain.Configuration;

/// <summary>
/// Azure OpenAI configuration for the conversational trade agent (and OCR field
/// normalization). The API key is supplied out-of-band (user-secrets locally,
/// Key Vault / App Service settings in the cloud) and is never committed.
/// When <see cref="Enabled"/> is false or the key is missing the agent UI is
/// hidden and the app keeps working without any AI dependency.
/// </summary>
public sealed class AgentOptions
{
    public const string SectionName = "Agent";

    public bool Enabled { get; set; }

    /// <summary>Azure OpenAI resource endpoint, e.g. https://name.openai.azure.com.</summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>Chat deployment name (e.g. gpt-5.4).</summary>
    public string Deployment { get; set; } = string.Empty;

    /// <summary>API key. Provided via user-secrets / Key Vault, not appsettings.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Whether the agent is fully configured and turned on.</summary>
    public bool IsConfigured =>
        Enabled
        && !string.IsNullOrWhiteSpace(Endpoint)
        && !string.IsNullOrWhiteSpace(Deployment)
        && !string.IsNullOrWhiteSpace(ApiKey);
}

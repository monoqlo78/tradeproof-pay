namespace HashKeyChain.Services.Documents;

/// <summary>
/// Abstraction over private document storage. Original files are stored privately
/// (never on-chain, spec §20); only their SHA-256 hashes are anchored on-chain.
/// DemoMode uses local disk; a cloud implementation can use blob storage.
/// </summary>
public interface IDocumentStorage
{
    /// <summary>Persists file content and returns an opaque relative storage path.</summary>
    Task<string> SaveAsync(int tradeId, string relativeName, byte[] content, CancellationToken ct = default);

    Task<byte[]?> ReadAsync(string storagePath, CancellationToken ct = default);
}

/// <summary>Options for <see cref="LocalFileDocumentStorage"/>.</summary>
public sealed class DocumentStorageOptions
{
    public const string SectionName = "DocumentStorage";

    /// <summary>Base directory for stored documents. Defaults to a folder under the app content root.</summary>
    public string BasePath { get; set; } = "App_Data/documents";
}

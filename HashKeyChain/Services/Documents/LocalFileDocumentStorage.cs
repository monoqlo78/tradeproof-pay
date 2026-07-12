using Microsoft.Extensions.Options;

namespace HashKeyChain.Services.Documents;

/// <summary>
/// Stores documents on the local file system under a configured base path. Files
/// are laid out as <c>{BasePath}/trade-{id}/{relativeName}</c>. Path traversal in
/// the supplied name is neutralised.
/// </summary>
public sealed class LocalFileDocumentStorage : IDocumentStorage
{
    private readonly string _basePath;

    public LocalFileDocumentStorage(IOptions<DocumentStorageOptions> options, IHostEnvironment env)
    {
        var configured = options.Value.BasePath;
        _basePath = Path.IsPathRooted(configured)
            ? configured
            : Path.Combine(env.ContentRootPath, configured);
    }

    public async Task<string> SaveAsync(int tradeId, string relativeName, byte[] content, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        var safeName = Path.GetFileName(relativeName); // strip any directory components
        var relative = Path.Combine($"trade-{tradeId}", safeName);
        var fullPath = Path.Combine(_basePath, relative);

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await File.WriteAllBytesAsync(fullPath, content, ct);
        return relative.Replace('\\', '/');
    }

    public async Task<byte[]?> ReadAsync(string storagePath, CancellationToken ct = default)
    {
        var fullPath = Path.Combine(_basePath, storagePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(fullPath))
            return null;
        return await File.ReadAllBytesAsync(fullPath, ct);
    }
}

using System.Security.Cryptography;
using HashKeyChain.Data;
using HashKeyChain.Domain;
using HashKeyChain.Localization;
using HashKeyChain.Services.Trades;
using Microsoft.EntityFrameworkCore;

namespace HashKeyChain.Services.Documents;

/// <summary>
/// Handles seller document submission with versioning and integrity hashing
/// (spec §8). Re-uploading the same document type never deletes prior versions;
/// it supersedes them and adds a new current version. The SHA-256 of the original
/// file is computed and stored (the value later anchored on-chain, §20).
/// </summary>
public interface IDocumentService
{
    Task<DocumentVersion> UploadAsync(
        int tradeId, DocumentType documentType, string fileName, byte[] content, string actor, CancellationToken ct = default);

    Task<IReadOnlyList<TradeDocument>> GetDocumentsAsync(int tradeId, CancellationToken ct = default);

    /// <summary>Whether every required document type has a current version (spec §2).</summary>
    Task<bool> HasAllRequiredAsync(int tradeId, CancellationToken ct = default);

    Task<IReadOnlyList<DocumentType>> MissingRequiredAsync(int tradeId, CancellationToken ct = default);
}

public sealed class DocumentService(
    IDbContextFactory<AppDbContext> factory,
    IDocumentStorage storage,
    ITradeStateMachine stateMachine,
    IAuditWriter audit) : IDocumentService
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;
    private readonly IDocumentStorage _storage = storage;
    private readonly ITradeStateMachine _stateMachine = stateMachine;
    private readonly IAuditWriter _audit = audit;

    public async Task<DocumentVersion> UploadAsync(
        int tradeId, DocumentType documentType, string fileName, byte[] content, string actor, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (content.Length == 0)
            throw new TradeOperationException("Uploaded file is empty.");
        if (string.IsNullOrWhiteSpace(fileName))
            throw new TradeOperationException("File name is required.");

        await using var db = await _factory.CreateDbContextAsync(ct);
        var trade = await db.Trades
            .Include(t => t.Documents).ThenInclude(d => d.Versions)
            .FirstOrDefaultAsync(t => t.Id == tradeId, ct)
            ?? throw new TradeOperationException($"Trade {tradeId} was not found.");

        if (trade.Status is not (TradeStatus.AwaitingDocuments or TradeStatus.DocumentsRejected
            or TradeStatus.ManualReview or TradeStatus.Analyzing))
            throw new TradeOperationException($"Documents cannot be uploaded in state {trade.Status}.");

        var sha256 = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

        var slot = trade.Documents.FirstOrDefault(d => d.DocumentType == documentType);
        if (slot is null)
        {
            slot = new TradeDocument
            {
                TradeId = tradeId,
                DocumentType = documentType,
                IsRequired = RequiredDocuments.IsRequired(trade.TransportMode, documentType)
            };
            db.TradeDocuments.Add(slot);
        }

        var priorCurrent = slot.Versions.Where(v => v.IsCurrent).ToList();
        foreach (var v in priorCurrent)
            v.IsCurrent = false;

        var nextVersion = slot.Versions.Count == 0 ? 1 : slot.Versions.Max(v => v.Version) + 1;
        var storagePath = await _storage.SaveAsync(tradeId, $"{documentType}-v{nextVersion}-{Path.GetFileName(fileName)}", content, ct);

        var version = new DocumentVersion
        {
            Version = nextVersion,
            FileName = Path.GetFileName(fileName),
            StoragePath = storagePath,
            UploadedBy = actor,
            UploadedAtUtc = DateTime.UtcNow,
            Sha256 = sha256,
            AnalysisStatus = AnalysisStatus.Pending,
            IsCurrent = true
        };
        slot.Versions.Add(version);

        // A resubmission after rejection returns the trade to AwaitingDocuments so
        // it can be re-analyzed (spec §8).
        if (trade.Status == TradeStatus.DocumentsRejected)
            _stateMachine.Transition(trade, TradeStatus.AwaitingDocuments);

        await db.SaveChangesAsync(ct);

        if (priorCurrent.Count > 0)
            await _audit.WriteAsync(AuditAction.DocumentSuperseded, actor, tradeId,
                comment: $"{documentType} v{priorCurrent.Max(v => v.Version)} superseded.", ct: ct);

        await _audit.WriteAsync(AuditAction.DocumentUploaded, actor, tradeId,
            comment: $"{documentType} v{nextVersion} uploaded (sha256 {sha256[..12]}…).", ct: ct);

        return version;
    }

    public async Task<IReadOnlyList<TradeDocument>> GetDocumentsAsync(int tradeId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.TradeDocuments
            .Where(d => d.TradeId == tradeId)
            .Include(d => d.Versions)
            .AsNoTracking()
            .ToListAsync(ct);
    }

    public async Task<bool> HasAllRequiredAsync(int tradeId, CancellationToken ct = default) =>
        (await MissingRequiredAsync(tradeId, ct)).Count == 0;

    public async Task<IReadOnlyList<DocumentType>> MissingRequiredAsync(int tradeId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var trade = await db.Trades
            .Include(t => t.Documents).ThenInclude(d => d.Versions)
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == tradeId, ct)
            ?? throw new TradeOperationException($"Trade {tradeId} was not found.");

        var required = RequiredDocuments.For(trade.TransportMode);
        return required
            .Where(rt => !trade.Documents.Any(d => d.DocumentType == rt && d.Versions.Any(v => v.IsCurrent)))
            .ToList();
    }
}

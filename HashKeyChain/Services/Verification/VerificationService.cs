using System.Text.Json;
using HashKeyChain.Data;
using HashKeyChain.Domain;
using HashKeyChain.Localization;
using HashKeyChain.Services.Analysis;
using HashKeyChain.Services.Documents;
using HashKeyChain.Services.Trades;
using Microsoft.EntityFrameworkCore;

namespace HashKeyChain.Services.Verification;

/// <summary>
/// Runs analysis + the deterministic rule engine over a trade's current documents
/// and records the verdict (spec §10–§12), then exposes the human verifier
/// actions (confirm / reject / raise manual review, spec §12/§13). The verifier
/// can never sign a payment (separation of duties, §4) — that is a separate role.
/// </summary>
public interface IVerificationService
{
    Task<VerificationRun> RunAnalysisAsync(int tradeId, string actor, CancellationToken ct = default);
    Task<Trade> ConfirmAsync(int tradeId, string verifierActor, string? comment = null, CancellationToken ct = default);
    Task<Trade> RejectAsync(int tradeId, string actor, string reason, CancellationToken ct = default);
    Task<Trade> RaiseManualReviewAsync(int tradeId, string actor, string? comment = null, CancellationToken ct = default);
}

public sealed class VerificationService(
    IDbContextFactory<AppDbContext> factory,
    ITradeStateMachine stateMachine,
    IDocumentStorage storage,
    IDocumentAnalysisService analyzer,
    ITradeRuleEngine ruleEngine,
    IAuditWriter audit) : IVerificationService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    private readonly IDbContextFactory<AppDbContext> _factory = factory;
    private readonly ITradeStateMachine _stateMachine = stateMachine;
    private readonly IDocumentStorage _storage = storage;
    private readonly IDocumentAnalysisService _analyzer = analyzer;
    private readonly ITradeRuleEngine _ruleEngine = ruleEngine;
    private readonly IAuditWriter _audit = audit;

    public async Task<VerificationRun> RunAnalysisAsync(int tradeId, string actor, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var trade = await db.Trades
            .Include(t => t.BuyerCompany)
            .Include(t => t.SellerCompany)
            .Include(t => t.Documents).ThenInclude(d => d.Versions)
            .FirstOrDefaultAsync(t => t.Id == tradeId, ct)
            ?? throw new TradeOperationException($"Trade {tradeId} was not found.");

        if (trade.Status is not TradeStatus.AwaitingDocuments)
            throw new TradeOperationException($"Analysis can only run from AwaitingDocuments (was {trade.Status}). Resubmit documents first.");

        _stateMachine.Transition(trade, TradeStatus.Analyzing);
        await db.SaveChangesAsync(ct);

        // Analyze each current document version.
        var analyzed = new List<AnalyzedDocument>();
        foreach (var slot in trade.Documents)
        {
            var current = slot.Versions.Where(v => v.IsCurrent).OrderByDescending(v => v.Version).FirstOrDefault();
            if (current is null)
                continue;

            var content = current.StoragePath is null ? Array.Empty<byte>()
                : await _storage.ReadAsync(current.StoragePath, ct) ?? Array.Empty<byte>();

            var result = await _analyzer.AnalyzeAsync(slot.DocumentType, current.FileName, content, trade, ct);

            current.AnalysisStatus = AnalysisStatus.Completed;
            current.Confidence = result.Confidence;
            current.DetectedType = result.DetectedType;
            current.SourceLanguage = result.SourceLanguage;
            current.ExtractedFieldsJson = JsonSerializer.Serialize(result.Fields, JsonOptions);

            analyzed.Add(new AnalyzedDocument(slot.DocumentType, result.DetectedType, result.Confidence, result.Fields));
        }

        var context = new RuleContext(trade, trade.BuyerCompany?.Name, trade.SellerCompany?.Name, analyzed);
        var run = _ruleEngine.Evaluate(context);
        run.TradeId = trade.Id;
        db.VerificationRuns.Add(run);

        trade.LatestVerdict = run.Result;
        var before = trade.Status;
        var target = run.Result switch
        {
            VerificationResult.Pass => TradeStatus.ReadyForVerification,
            VerificationResult.ManualReview => TradeStatus.ManualReview,
            _ => TradeStatus.Blocked
        };
        _stateMachine.Transition(trade, target);
        await db.SaveChangesAsync(ct);

        await _audit.WriteAsync(AuditAction.DocumentAnalyzed, actor, trade.Id,
            before: before, after: trade.Status, comment: run.Summary, ct: ct);

        if (run.Result == VerificationResult.Blocked)
            await _audit.WriteAsync(AuditAction.MismatchDetected, actor, trade.Id,
                comment: run.Summary, ct: ct);

        return run;
    }

    public async Task<Trade> ConfirmAsync(int tradeId, string verifierActor, string? comment = null, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var trade = await Load(db, tradeId, ct);

        if (trade.Status is not (TradeStatus.ReadyForVerification or TradeStatus.ManualReview))
            throw new TradeOperationException($"Only a ReadyForVerification/ManualReview trade can be confirmed (was {trade.Status}).");
        if (trade.LatestVerdict == VerificationResult.Blocked)
            throw new TradeOperationException("A blocked trade cannot be confirmed; the documents must be corrected.");

        var before = trade.Status;
        // ManualReview must first return to ReadyForVerification, then to approval.
        if (trade.Status == TradeStatus.ManualReview)
            _stateMachine.Transition(trade, TradeStatus.ReadyForVerification);
        _stateMachine.Transition(trade, TradeStatus.ReadyForApproval);
        trade.LatestVerdict = VerificationResult.ReadyForApproval;
        await db.SaveChangesAsync(ct);

        await _audit.WriteAsync(AuditAction.DocumentVerified, verifierActor, trade.Id,
            before: before, after: trade.Status, comment: comment ?? "Documents verified by trade verifier.", ct: ct);

        return trade;
    }

    public async Task<Trade> RejectAsync(int tradeId, string actor, string reason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new TradeOperationException("A rejection reason is required.");

        await using var db = await _factory.CreateDbContextAsync(ct);
        var trade = await db.Trades
            .Include(t => t.Documents).ThenInclude(d => d.Versions)
            .FirstOrDefaultAsync(t => t.Id == tradeId, ct)
            ?? throw new TradeOperationException($"Trade {tradeId} was not found.");

        var before = trade.Status;
        _stateMachine.Transition(trade, TradeStatus.DocumentsRejected);

        foreach (var version in trade.Documents.SelectMany(d => d.Versions).Where(v => v.IsCurrent))
            version.RejectionReason = reason;

        await db.SaveChangesAsync(ct);

        await _audit.WriteAsync(AuditAction.DocumentRejected, actor, trade.Id,
            before: before, after: trade.Status, comment: reason, ct: ct);

        return trade;
    }

    public async Task<Trade> RaiseManualReviewAsync(int tradeId, string actor, string? comment = null, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var trade = await Load(db, tradeId, ct);

        var before = trade.Status;
        _stateMachine.Transition(trade, TradeStatus.ManualReview);
        trade.LatestVerdict = VerificationResult.ManualReview;
        await db.SaveChangesAsync(ct);

        await _audit.WriteAsync(AuditAction.ManualReviewRaised, actor, trade.Id,
            before: before, after: trade.Status, comment: comment ?? "Manual review raised.", ct: ct);

        return trade;
    }

    private static async Task<Trade> Load(AppDbContext db, int tradeId, CancellationToken ct) =>
        await db.Trades.FirstOrDefaultAsync(t => t.Id == tradeId, ct)
        ?? throw new TradeOperationException($"Trade {tradeId} was not found.");
}

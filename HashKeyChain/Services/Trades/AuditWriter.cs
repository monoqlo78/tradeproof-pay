using HashKeyChain.Data;
using HashKeyChain.Domain;
using HashKeyChain.Localization;
using Microsoft.EntityFrameworkCore;

namespace HashKeyChain.Services.Trades;

/// <summary>
/// Appends entries to the immutable audit trail (spec §21). This is the only
/// supported way to write audit rows; nothing ever updates or deletes them.
/// </summary>
public interface IAuditWriter
{
    Task WriteAsync(AuditEntry entry, CancellationToken ct = default);

    Task WriteAsync(
        AuditAction action,
        string actor,
        int? tradeId = null,
        TradeStatus? before = null,
        TradeStatus? after = null,
        string? comment = null,
        string? correlationId = null,
        string? transactionHash = null,
        CancellationToken ct = default);
}

public sealed class AuditWriter(IDbContextFactory<AppDbContext> factory) : IAuditWriter
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task WriteAsync(AuditEntry entry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.TimestampUtc == default)
            entry.TimestampUtc = DateTime.UtcNow;

        await using var db = await _factory.CreateDbContextAsync(ct);
        db.AuditEntries.Add(entry);
        await db.SaveChangesAsync(ct);
    }

    public Task WriteAsync(
        AuditAction action,
        string actor,
        int? tradeId = null,
        TradeStatus? before = null,
        TradeStatus? after = null,
        string? comment = null,
        string? correlationId = null,
        string? transactionHash = null,
        CancellationToken ct = default) =>
        WriteAsync(new AuditEntry
        {
            Action = action,
            Actor = actor,
            TradeId = tradeId,
            BeforeStatus = before,
            AfterStatus = after,
            Comment = comment,
            CorrelationId = correlationId,
            TransactionHash = transactionHash,
            TimestampUtc = DateTime.UtcNow
        }, ct);
}

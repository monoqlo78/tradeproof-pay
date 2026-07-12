using HashKeyChain.Data;
using HashKeyChain.Domain;
using HashKeyChain.Localization;
using HashKeyChain.Services.Blockchain;
using HashKeyChain.Services.Trades;
using Microsoft.EntityFrameworkCore;

namespace HashKeyChain.Services.Settlement;

/// <summary>
/// Handles trade expiry and buyer refund (spec §18). Once a funded trade passes
/// its payment expiry without settlement it can be expired and the escrow
/// refunded to the buyer. A settled trade can never be refunded, and a second
/// refund is always rejected. The trade is only marked
/// <see cref="TradeStatus.Refunded"/> once the refund receipt is confirmed.
/// </summary>
public interface IRefundService
{
    Task<Trade> MarkExpiredAsync(int tradeId, string actor, bool force = false, CancellationToken ct = default);
    Task<Trade> RefundAsync(int tradeId, string actor, CancellationToken ct = default);
}

public sealed class RefundService(
    IDbContextFactory<AppDbContext> factory,
    ITradeStateMachine stateMachine,
    IEscrowChainService chain,
    IAuditWriter audit) : IRefundService
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;
    private readonly ITradeStateMachine _stateMachine = stateMachine;
    private readonly IEscrowChainService _chain = chain;
    private readonly IAuditWriter _audit = audit;

    public async Task<Trade> MarkExpiredAsync(int tradeId, string actor, bool force = false, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var trade = await Load(db, tradeId, ct);

        if (trade.IsSettled)
            throw new TradeOperationException("A settled trade cannot expire.");
        if (trade.Status == TradeStatus.Expired)
            return trade;
        if (!force && !SettlementService.IsExpired(trade))
            throw new TradeOperationException("Trade has not passed its payment expiry yet.");
        if (!_stateMachine.CanTransition(trade.Status, TradeStatus.Expired))
            throw new TradeOperationException($"Trade in state {trade.Status} cannot expire.");

        var before = trade.Status;
        _stateMachine.Transition(trade, TradeStatus.Expired);
        await db.SaveChangesAsync(ct);

        await _audit.WriteAsync(AuditAction.TradeConditionsChanged, actor, trade.Id,
            before: before, after: trade.Status, comment: "Trade expired (payment window elapsed).", ct: ct);

        return trade;
    }

    public async Task<Trade> RefundAsync(int tradeId, string actor, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var trade = await Load(db, tradeId, ct);

        if (trade.IsSettled)
            throw new TradeOperationException("A settled trade cannot be refunded.");
        if (trade.IsRefunded)
            throw new TradeOperationException("Trade is already refunded (double refund rejected).");
        if (!trade.IsFunded)
            throw new TradeOperationException("Trade is not funded; nothing to refund.");

        if (trade.Status == TradeStatus.Expired)
            _stateMachine.Transition(trade, TradeStatus.RefundPending);
        if (trade.Status != TradeStatus.RefundPending)
            throw new TradeOperationException($"Refund can only run from Expired/RefundPending (was {trade.Status}).");

        var correlationId = Guid.NewGuid().ToString("N");
        await db.SaveChangesAsync(ct);
        await _audit.WriteAsync(AuditAction.WalletOperationStarted, actor, trade.Id,
            comment: "Refund started.", correlationId: correlationId, ct: ct);

        var result = await _chain.RefundAsync(trade.Id, trade.BuyerWalletAddress, ct);
        db.ChainTransactions.Add(SettlementService.ToChainTransaction(trade.Id, "Refund", result));
        await db.SaveChangesAsync(ct);

        await _audit.WriteAsync(AuditAction.TransactionSubmitted, actor, trade.Id,
            comment: $"Refund tx submitted ({result.Status}).", correlationId: correlationId,
            transactionHash: result.TransactionHash, ct: ct);

        if (!result.Success || !string.Equals(result.Status, "Success", StringComparison.OrdinalIgnoreCase))
        {
            _stateMachine.Transition(trade, TradeStatus.Expired);
            await db.SaveChangesAsync(ct);
            await _audit.WriteAsync(AuditAction.SettlementFailed, actor, trade.Id,
                after: trade.Status, comment: result.Error ?? "Refund receipt not confirmed.",
                correlationId: correlationId, transactionHash: result.TransactionHash, ct: ct);
            throw new TradeOperationException($"Refund failed: {result.Error ?? result.Status}");
        }

        await _audit.WriteAsync(AuditAction.ReceiptConfirmed, actor, trade.Id,
            comment: "Refund receipt confirmed successful.", correlationId: correlationId,
            transactionHash: result.TransactionHash, ct: ct);

        trade.IsRefunded = true;
        trade.RefundTxHash = result.TransactionHash;
        var before = trade.Status;
        _stateMachine.Transition(trade, TradeStatus.Refunded);
        await db.SaveChangesAsync(ct);

        await _audit.WriteAsync(AuditAction.Refunded, actor, trade.Id,
            before: before, after: trade.Status,
            comment: $"Refunded {trade.PaymentAmount} {trade.PaymentToken} to buyer.",
            correlationId: correlationId, transactionHash: result.TransactionHash, ct: ct);

        return trade;
    }

    private static async Task<Trade> Load(AppDbContext db, int tradeId, CancellationToken ct) =>
        await db.Trades.FirstOrDefaultAsync(t => t.Id == tradeId, ct)
        ?? throw new TradeOperationException($"Trade {tradeId} was not found.");
}

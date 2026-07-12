using HashKeyChain.Data;
using HashKeyChain.Domain;
using HashKeyChain.Localization;
using HashKeyChain.Services.Blockchain;
using HashKeyChain.Services.Trades;
using Microsoft.EntityFrameworkCore;

namespace HashKeyChain.Services.Settlement;

/// <summary>
/// Final payment approval and on-chain settlement (spec §14–§17). The buyer
/// approver — never the verifier (separation of duties, §4) — approves payment,
/// then settlement releases the escrow to the seller. A trade is only marked
/// <see cref="TradeStatus.Settled"/> once the release receipt is confirmed
/// successful (chain doc §4/§17); a pending or failed receipt never settles it,
/// and a second settlement is always rejected.
/// </summary>
public interface ISettlementService
{
    Task<Trade> ApprovePaymentAsync(int tradeId, string approverActor, string? comment = null, CancellationToken ct = default);
    Task<Trade> SettleAsync(int tradeId, string approverActor, CancellationToken ct = default);
}

public sealed class SettlementService(
    IDbContextFactory<AppDbContext> factory,
    ITradeStateMachine stateMachine,
    IEscrowChainService chain,
    IAuditWriter audit) : ISettlementService
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;
    private readonly ITradeStateMachine _stateMachine = stateMachine;
    private readonly IEscrowChainService _chain = chain;
    private readonly IAuditWriter _audit = audit;

    public async Task<Trade> ApprovePaymentAsync(int tradeId, string approverActor, string? comment = null, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var trade = await Load(db, tradeId, ct);

        if (trade.Status != TradeStatus.ReadyForApproval)
            throw new TradeOperationException($"Payment can only be approved from ReadyForApproval (was {trade.Status}).");
        if (trade.LatestVerdict != VerificationResult.ReadyForApproval)
            throw new TradeOperationException("Documents must be verified (ReadyForApproval verdict) before payment approval (§14).");
        if (!trade.IsFunded)
            throw new TradeOperationException("Trade is not funded.");
        if (trade.IsSettled)
            throw new TradeOperationException("Trade is already settled.");
        if (IsExpired(trade))
            throw new TradeOperationException("Payment window has expired; the trade must be refunded (§18).");

        var before = trade.Status;
        _stateMachine.Transition(trade, TradeStatus.Approved);
        await db.SaveChangesAsync(ct);

        await _audit.WriteAsync(AuditAction.PaymentApproved, approverActor, trade.Id,
            before: before, after: trade.Status, comment: comment ?? "Payment approved.", ct: ct);

        return trade;
    }

    public async Task<Trade> SettleAsync(int tradeId, string approverActor, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var trade = await Load(db, tradeId, ct);

        if (trade.Status != TradeStatus.Approved)
            throw new TradeOperationException($"Settlement can only run from Approved (was {trade.Status}).");
        if (trade.IsSettled)
            throw new TradeOperationException("Trade is already settled (double settlement rejected).");
        if (trade.IsRefunded)
            throw new TradeOperationException("Trade has been refunded and cannot be settled.");
        if (IsExpired(trade))
            throw new TradeOperationException("Payment window has expired; the trade must be refunded (§18).");

        var correlationId = Guid.NewGuid().ToString("N");
        var before = trade.Status;
        _stateMachine.Transition(trade, TradeStatus.SettlementPending);
        await db.SaveChangesAsync(ct);

        await _audit.WriteAsync(AuditAction.WalletOperationStarted, approverActor, trade.Id,
            before: before, after: trade.Status, comment: "Settlement started.", correlationId: correlationId, ct: ct);

        var result = await _chain.ReleaseAsync(trade.Id, trade.SellerWalletAddress, trade.PaymentAmount, ct);
        db.ChainTransactions.Add(ToChainTransaction(trade.Id, "Release", result));
        await db.SaveChangesAsync(ct);

        await _audit.WriteAsync(AuditAction.TransactionSubmitted, approverActor, trade.Id,
            comment: $"Release tx submitted ({result.Status}).", correlationId: correlationId,
            transactionHash: result.TransactionHash, ct: ct);

        // Receipt-confirmed-before-Settled (chain doc §4/§17): a non-success receipt
        // rolls the trade back to ReadyForApproval so it can be retried, and never
        // marks it settled.
        if (!result.Success || !string.Equals(result.Status, "Success", StringComparison.OrdinalIgnoreCase))
        {
            _stateMachine.Transition(trade, TradeStatus.ReadyForApproval);
            await db.SaveChangesAsync(ct);
            await _audit.WriteAsync(AuditAction.SettlementFailed, approverActor, trade.Id,
                after: trade.Status, comment: result.Error ?? "Settlement receipt not confirmed.",
                correlationId: correlationId, transactionHash: result.TransactionHash, ct: ct);
            throw new TradeOperationException($"Settlement failed: {result.Error ?? result.Status}");
        }

        await _audit.WriteAsync(AuditAction.ReceiptConfirmed, approverActor, trade.Id,
            comment: "Release receipt confirmed successful.", correlationId: correlationId,
            transactionHash: result.TransactionHash, ct: ct);

        trade.IsSettled = true;
        trade.SettlementTxHash = result.TransactionHash;
        var beforeSettle = trade.Status;
        _stateMachine.Transition(trade, TradeStatus.Settled);
        await db.SaveChangesAsync(ct);

        await _audit.WriteAsync(AuditAction.SettlementCompleted, approverActor, trade.Id,
            before: beforeSettle, after: trade.Status,
            comment: $"Settled: {trade.PaymentAmount} {trade.PaymentToken} released to seller.",
            correlationId: correlationId, transactionHash: result.TransactionHash, ct: ct);

        return trade;
    }

    internal static bool IsExpired(Trade trade) =>
        trade.PaymentExpiry is { } expiry && DateTime.UtcNow > expiry;

    internal static ChainTransaction ToChainTransaction(int tradeId, string operation, ChainTxResult result) => new()
    {
        TradeId = tradeId,
        Operation = operation,
        TransactionHash = result.TransactionHash,
        BlockNumber = result.BlockNumber,
        FromAddress = result.FromAddress,
        ToAddress = result.ToAddress,
        ContractAddress = result.ContractAddress,
        ChainId = result.ChainId,
        GasUsed = result.GasUsed,
        Status = result.Status,
        ExplorerUrl = result.ExplorerUrl,
        TimestampUtc = DateTime.UtcNow
    };

    private static async Task<Trade> Load(AppDbContext db, int tradeId, CancellationToken ct) =>
        await db.Trades.FirstOrDefaultAsync(t => t.Id == tradeId, ct)
        ?? throw new TradeOperationException($"Trade {tradeId} was not found.");
}

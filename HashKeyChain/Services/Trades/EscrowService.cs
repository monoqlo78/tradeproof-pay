using HashKeyChain.Data;
using HashKeyChain.Domain;
using HashKeyChain.Localization;
using HashKeyChain.Services.Blockchain;
using Microsoft.EntityFrameworkCore;

namespace HashKeyChain.Services.Trades;

/// <summary>
/// Orchestrates escrow funding (spec §7). Calls the escrow chain service (mock in
/// DemoMode), records the resulting <see cref="ChainTransaction"/>, and — only on
/// a confirmed successful receipt — marks the trade funded and advances it to
/// AwaitingDocuments. The buyer wallet and locked amount are always used; a funded
/// trade can never silently re-fund (the mock rejects a second fund).
/// </summary>
public interface IEscrowService
{
    Task<Trade> FundAsync(int tradeId, string actor, CancellationToken ct = default);
}

public sealed class EscrowService(
    IDbContextFactory<AppDbContext> factory,
    ITradeStateMachine stateMachine,
    IEscrowChainService chain,
    IAuditWriter audit) : IEscrowService
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;
    private readonly ITradeStateMachine _stateMachine = stateMachine;
    private readonly IEscrowChainService _chain = chain;
    private readonly IAuditWriter _audit = audit;

    public async Task<Trade> FundAsync(int tradeId, string actor, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var trade = await db.Trades.FirstOrDefaultAsync(t => t.Id == tradeId, ct)
                    ?? throw new TradeOperationException($"Trade {tradeId} was not found.");

        if (trade.Status != TradeStatus.AwaitingFunding)
            throw new TradeOperationException($"Escrow can only be funded from AwaitingFunding (was {trade.Status}).");
        if (!trade.ConditionsLocked)
            throw new TradeOperationException("Trade conditions must be approved and locked before funding (§6).");
        if (trade.IsFunded)
            throw new TradeOperationException("Trade is already funded.");

        var correlationId = Guid.NewGuid().ToString("N");
        await _audit.WriteAsync(AuditAction.WalletOperationStarted, actor, trade.Id,
            comment: "Escrow funding started.", correlationId: correlationId, ct: ct);

        var result = await _chain.FundAsync(
            trade.Id, trade.BuyerWalletAddress, trade.PaymentAmount, trade.PaymentToken, ct);

        RecordTransaction(db, trade.Id, result);
        await db.SaveChangesAsync(ct);

        await _audit.WriteAsync(AuditAction.TransactionSubmitted, actor, trade.Id,
            comment: $"Fund tx submitted ({result.Status}).", correlationId: correlationId,
            transactionHash: result.TransactionHash, ct: ct);

        if (!result.Success)
            throw new TradeOperationException($"Escrow funding failed: {result.Error}");

        // Only treat as funded once the receipt is confirmed successful (§4/§17).
        if (!string.Equals(result.Status, "Success", StringComparison.OrdinalIgnoreCase))
            throw new TradeOperationException($"Fund receipt not confirmed (status {result.Status}).");

        var before = trade.Status;
        trade.IsFunded = true;
        _stateMachine.Transition(trade, TradeStatus.Funded);
        // Immediately await the seller's documents.
        _stateMachine.Transition(trade, TradeStatus.AwaitingDocuments);
        await db.SaveChangesAsync(ct);

        await _audit.WriteAsync(AuditAction.EscrowFunded, actor, trade.Id,
            before: before, after: trade.Status,
            comment: $"Escrow funded with {trade.PaymentAmount} {trade.PaymentToken}.",
            correlationId: correlationId, transactionHash: result.TransactionHash, ct: ct);

        return trade;
    }

    internal static void RecordTransaction(AppDbContext db, int tradeId, ChainTxResult result)
    {
        db.ChainTransactions.Add(new ChainTransaction
        {
            TradeId = tradeId,
            Operation = "Fund",
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
        });
    }
}

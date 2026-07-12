using HashKeyChain.Data;
using HashKeyChain.Domain;
using HashKeyChain.Localization;
using Microsoft.EntityFrameworkCore;

namespace HashKeyChain.Services.Trades;

public sealed class TradeService(
    IDbContextFactory<AppDbContext> factory,
    ITradeStateMachine stateMachine,
    IAuditWriter audit) : ITradeService
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;
    private readonly ITradeStateMachine _stateMachine = stateMachine;
    private readonly IAuditWriter _audit = audit;

    public async Task<Trade> CreateDraftAsync(TradeConditionsInput input, string actor, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        Validate(input);

        await using var db = await _factory.CreateDbContextAsync(ct);

        var now = DateTime.UtcNow;
        var trade = new Trade
        {
            TradeReference = await GenerateReferenceAsync(db, now, ct),
            Status = TradeStatus.Draft,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            CreatedBy = actor
        };
        Apply(trade, input);

        db.Trades.Add(trade);
        await db.SaveChangesAsync(ct);

        await _audit.WriteAsync(AuditAction.TradeCreated, actor, trade.Id,
            after: trade.Status, comment: $"Trade {trade.TradeReference} created.", ct: ct);

        return trade;
    }

    public async Task<Trade> UpdateConditionsAsync(int tradeId, TradeConditionsInput input, string actor, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        Validate(input);

        await using var db = await _factory.CreateDbContextAsync(ct);
        var trade = await LoadAsync(db, tradeId, ct);

        if (trade.Status is not (TradeStatus.Draft or TradeStatus.PendingTradeApproval))
            throw new TradeOperationException($"Conditions can only be edited while Draft or PendingTradeApproval (was {trade.Status}).");

        if (trade.ConditionsLocked)
            throw new TradeOperationException("Trade conditions are locked after approval and cannot be edited (§6).");

        Apply(trade, input);
        trade.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        await _audit.WriteAsync(AuditAction.TradeConditionsChanged, actor, trade.Id,
            comment: "Trade conditions updated.", ct: ct);

        return trade;
    }

    public async Task<Trade> RequestApprovalAsync(int tradeId, string actor, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var trade = await LoadAsync(db, tradeId, ct);

        var before = trade.Status;
        _stateMachine.Transition(trade, TradeStatus.PendingTradeApproval);
        await db.SaveChangesAsync(ct);

        await _audit.WriteAsync(AuditAction.TradeConditionsChanged, actor, trade.Id,
            before: before, after: trade.Status, comment: "Approval requested.", ct: ct);

        return trade;
    }

    public async Task<Trade> ApproveConditionsAsync(int tradeId, string actor, string? comment = null, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var trade = await LoadAsync(db, tradeId, ct);

        var before = trade.Status;
        _stateMachine.Transition(trade, TradeStatus.AwaitingFunding);
        // Locking the conditions is the whole point of approval (§6).
        trade.ConditionsLocked = true;
        await db.SaveChangesAsync(ct);

        await _audit.WriteAsync(AuditAction.TradeConditionsApproved, actor, trade.Id,
            before: before, after: trade.Status,
            comment: comment ?? "Trade conditions approved and locked.", ct: ct);

        return trade;
    }

    public async Task<Trade> CancelAsync(int tradeId, string actor, string? reason = null, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var trade = await LoadAsync(db, tradeId, ct);

        if (trade.IsFunded && !trade.IsRefunded)
            throw new TradeOperationException("A funded trade cannot be cancelled; it must expire and be refunded (§18).");

        var before = trade.Status;
        _stateMachine.Transition(trade, TradeStatus.Cancelled);
        await db.SaveChangesAsync(ct);

        await _audit.WriteAsync(AuditAction.TradeCancelled, actor, trade.Id,
            before: before, after: trade.Status, comment: reason ?? "Trade cancelled.", ct: ct);

        return trade;
    }

    // ---- helpers ----

    private static async Task<Trade> LoadAsync(AppDbContext db, int tradeId, CancellationToken ct)
    {
        var trade = await db.Trades.FirstOrDefaultAsync(t => t.Id == tradeId, ct);
        return trade ?? throw new TradeOperationException($"Trade {tradeId} was not found.");
    }

    private static void Validate(TradeConditionsInput input)
    {
        if (input.BuyerCompanyId <= 0)
            throw new TradeOperationException("Buyer company is required.");
        if (input.SellerCompanyId <= 0)
            throw new TradeOperationException("Seller company is required.");
        if (input.BuyerCompanyId == input.SellerCompanyId)
            throw new TradeOperationException("Buyer and seller must be different companies.");
        if (string.IsNullOrWhiteSpace(input.SellerWalletAddress))
            throw new TradeOperationException("Seller wallet address is required and authoritative (§5).");
        if (input.PaymentAmount <= 0)
            throw new TradeOperationException("Payment amount must be greater than zero.");
    }

    private static void Apply(Trade trade, TradeConditionsInput input)
    {
        trade.BuyerCompanyId = input.BuyerCompanyId;
        trade.SellerCompanyId = input.SellerCompanyId;
        trade.BuyerWalletAddress = input.BuyerWalletAddress.Trim();
        // Seller wallet is authoritative and only ever set from explicit operator
        // input here — never derived from an uploaded invoice (§5).
        trade.SellerWalletAddress = input.SellerWalletAddress.Trim();
        trade.TransportMode = input.TransportMode;
        trade.PaymentToken = string.IsNullOrWhiteSpace(input.PaymentToken) ? "MockUSDC" : input.PaymentToken.Trim();
        trade.PaymentAmount = input.PaymentAmount;
        trade.Currency = string.IsNullOrWhiteSpace(input.Currency) ? "USDC" : input.Currency.Trim();
        trade.PurchaseOrderNumber = input.PurchaseOrderNumber?.Trim();
        trade.ExpectedProductDescription = input.ExpectedProductDescription?.Trim();
        trade.ExpectedQuantity = input.ExpectedQuantity;
        trade.LatestShipmentDate = input.LatestShipmentDate;
        trade.DocumentSubmissionDeadline = input.DocumentSubmissionDeadline;
        trade.PaymentExpiry = input.PaymentExpiry;
        trade.Notes = input.Notes?.Trim();
    }

    private static async Task<string> GenerateReferenceAsync(AppDbContext db, DateTime now, CancellationToken ct)
    {
        var prefix = $"TP-{now:yyyyMMdd}-";
        var todayCount = await db.Trades.CountAsync(t => t.TradeReference.StartsWith(prefix), ct);
        return $"{prefix}{todayCount + 1:D3}";
    }
}

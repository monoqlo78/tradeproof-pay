using HashKeyChain.Configuration;
using HashKeyChain.Data;
using HashKeyChain.Domain;
using HashKeyChain.Localization;
using HashKeyChain.Services.Blockchain;
using HashKeyChain.Services.Settlement;
using HashKeyChain.Services.Trades;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HashKeyChain.Tests;

/// <summary>
/// Tests for approval + settlement + refund (spec §14–§18): settlement only
/// completes on a confirmed receipt, double settlement is rejected, a settled
/// trade can't be refunded, and expiry leads to a single confirmed refund.
/// </summary>
public class SettlementServiceTests
{
    private sealed class InMemoryFactory(string name) : IDbContextFactory<AppDbContext>
    {
        private readonly DbContextOptions<AppDbContext> _options =
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(name).Options;
        public AppDbContext CreateDbContext() => new(_options);
    }

    private static int _nextId = 700_000;
    private static int UniqueId() => Interlocked.Increment(ref _nextId);

    private sealed record Harness(
        ISettlementService Settle, IRefundService Refund, IDbContextFactory<AppDbContext> Factory, int TradeId);

    private static Harness Build(TradeStatus status = TradeStatus.ReadyForApproval,
        VerificationResult verdict = VerificationResult.ReadyForApproval, DateTime? expiry = null)
    {
        var factory = new InMemoryFactory($"settle-{Guid.NewGuid():N}");
        var chain = new MockEscrowChainService(Options.Create(new BlockchainOptions()));
        var audit = new AuditWriter(factory);
        var sm = new TradeStateMachine();
        var settle = new SettlementService(factory, sm, chain, audit);
        var refund = new RefundService(factory, sm, chain, audit);

        var id = UniqueId();
        using (var db = factory.CreateDbContext())
        {
            db.Trades.Add(new Trade
            {
                Id = id,
                TradeReference = $"T-{id}",
                Status = status,
                LatestVerdict = verdict,
                ConditionsLocked = true,
                IsFunded = true,
                BuyerWalletAddress = "0xB",
                SellerWalletAddress = "0xS",
                PaymentAmount = 1000m,
                PaymentToken = "MockUSDC",
                PaymentExpiry = expiry
            });
            db.SaveChanges();
        }
        // Fund the mock escrow so release/refund can operate.
        chain.FundAsync(id, "0xB", 1000m, "MockUSDC").GetAwaiter().GetResult();
        return new Harness(settle, refund, factory, id);
    }

    [Fact]
    public async Task Approve_then_settle_marks_settled_with_tx()
    {
        var h = Build();
        await h.Settle.ApprovePaymentAsync(h.TradeId, "approver@demo");
        var settled = await h.Settle.SettleAsync(h.TradeId, "approver@demo");

        Assert.Equal(TradeStatus.Settled, settled.Status);
        Assert.True(settled.IsSettled);
        Assert.False(string.IsNullOrEmpty(settled.SettlementTxHash));

        await using var db = h.Factory.CreateDbContext();
        Assert.True(await db.AuditEntries.AnyAsync(a => a.TradeId == h.TradeId && a.Action == AuditAction.ReceiptConfirmed));
        Assert.True(await db.AuditEntries.AnyAsync(a => a.TradeId == h.TradeId && a.Action == AuditAction.SettlementCompleted));
    }

    [Fact]
    public async Task Approve_rejected_without_verified_verdict()
    {
        var h = Build(verdict: VerificationResult.ManualReview);
        await Assert.ThrowsAsync<TradeOperationException>(() => h.Settle.ApprovePaymentAsync(h.TradeId, "approver@demo"));
    }

    [Fact]
    public async Task Double_settlement_rejected()
    {
        var h = Build();
        await h.Settle.ApprovePaymentAsync(h.TradeId, "approver@demo");
        await h.Settle.SettleAsync(h.TradeId, "approver@demo");
        // Already Settled — a second settle must fail.
        await Assert.ThrowsAsync<TradeOperationException>(() => h.Settle.SettleAsync(h.TradeId, "approver@demo"));
    }

    [Fact]
    public async Task Settled_trade_cannot_be_refunded()
    {
        var h = Build();
        await h.Settle.ApprovePaymentAsync(h.TradeId, "approver@demo");
        await h.Settle.SettleAsync(h.TradeId, "approver@demo");
        await Assert.ThrowsAsync<TradeOperationException>(() => h.Refund.RefundAsync(h.TradeId, "approver@demo"));
    }

    [Fact]
    public async Task Expire_then_refund_marks_refunded()
    {
        var h = Build(status: TradeStatus.ReadyForApproval, expiry: DateTime.UtcNow.AddDays(-1));
        var expired = await h.Refund.MarkExpiredAsync(h.TradeId, "ops@demo");
        Assert.Equal(TradeStatus.Expired, expired.Status);

        var refunded = await h.Refund.RefundAsync(h.TradeId, "ops@demo");
        Assert.Equal(TradeStatus.Refunded, refunded.Status);
        Assert.True(refunded.IsRefunded);
        Assert.False(string.IsNullOrEmpty(refunded.RefundTxHash));
    }

    [Fact]
    public async Task Double_refund_rejected()
    {
        var h = Build(status: TradeStatus.ReadyForApproval, expiry: DateTime.UtcNow.AddDays(-1));
        await h.Refund.MarkExpiredAsync(h.TradeId, "ops@demo");
        await h.Refund.RefundAsync(h.TradeId, "ops@demo");
        await Assert.ThrowsAsync<TradeOperationException>(() => h.Refund.RefundAsync(h.TradeId, "ops@demo"));
    }

    [Fact]
    public async Task Expire_rejected_before_expiry_unless_forced()
    {
        var h = Build(expiry: DateTime.UtcNow.AddDays(1));
        await Assert.ThrowsAsync<TradeOperationException>(() => h.Refund.MarkExpiredAsync(h.TradeId, "ops@demo"));
        var expired = await h.Refund.MarkExpiredAsync(h.TradeId, "ops@demo", force: true);
        Assert.Equal(TradeStatus.Expired, expired.Status);
    }

    [Fact]
    public async Task Approve_rejected_when_expired()
    {
        var h = Build(expiry: DateTime.UtcNow.AddDays(-1));
        await Assert.ThrowsAsync<TradeOperationException>(() => h.Settle.ApprovePaymentAsync(h.TradeId, "approver@demo"));
    }
}

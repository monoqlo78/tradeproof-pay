using HashKeyChain.Configuration;
using HashKeyChain.Data;
using HashKeyChain.Domain;
using HashKeyChain.Localization;
using HashKeyChain.Services.Blockchain;
using HashKeyChain.Services.Trades;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HashKeyChain.Tests;

/// <summary>
/// Tests for escrow funding orchestration (spec §7): a trade only becomes Funded
/// after a confirmed successful receipt, funding advances it to AwaitingDocuments,
/// a chain transaction is recorded, and pre-conditions (locked, correct state) are
/// enforced.
/// </summary>
public class EscrowServiceTests
{
    private sealed class InMemoryFactory(string name) : IDbContextFactory<AppDbContext>
    {
        private readonly DbContextOptions<AppDbContext> _options =
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(name).Options;

        public AppDbContext CreateDbContext() => new(_options);
    }

    private static int _nextId = 100_000;
    private static int UniqueId() => Interlocked.Increment(ref _nextId);

    private static (IEscrowService svc, IDbContextFactory<AppDbContext> factory, int tradeId) Build(
        TradeStatus status = TradeStatus.AwaitingFunding, bool locked = true)
    {
        var factory = new InMemoryFactory($"escrow-{Guid.NewGuid():N}");
        var chain = new MockEscrowChainService(Options.Create(new BlockchainOptions()));
        var audit = new AuditWriter(factory);
        var svc = new EscrowService(factory, new TradeStateMachine(), chain, audit);

        var id = UniqueId();
        using (var db = factory.CreateDbContext())
        {
            db.Trades.Add(new Trade
            {
                Id = id,
                TradeReference = $"T-{id}",
                Status = status,
                ConditionsLocked = locked,
                BuyerWalletAddress = "0xBUYER",
                SellerWalletAddress = "0xSELLER",
                PaymentAmount = 1000m,
                PaymentToken = "MockUSDC"
            });
            db.SaveChanges();
        }
        return (svc, factory, id);
    }

    [Fact]
    public async Task Fund_marks_funded_and_advances_to_awaiting_documents()
    {
        var (svc, factory, id) = Build();
        var trade = await svc.FundAsync(id, "approver@demo");

        Assert.True(trade.IsFunded);
        Assert.Equal(TradeStatus.AwaitingDocuments, trade.Status);

        await using var db = factory.CreateDbContext();
        Assert.True(await db.ChainTransactions.AnyAsync(c => c.TradeId == id && c.Operation == "Fund"));
        Assert.True(await db.AuditEntries.AnyAsync(a => a.TradeId == id && a.Action == AuditAction.EscrowFunded));
    }

    [Fact]
    public async Task Fund_rejected_when_not_awaiting_funding()
    {
        var (svc, _, id) = Build(status: TradeStatus.Draft, locked: false);
        await Assert.ThrowsAsync<TradeOperationException>(() => svc.FundAsync(id, "approver@demo"));
    }

    [Fact]
    public async Task Fund_rejected_when_conditions_not_locked()
    {
        var (svc, _, id) = Build(locked: false);
        await Assert.ThrowsAsync<TradeOperationException>(() => svc.FundAsync(id, "approver@demo"));
    }

    [Fact]
    public async Task Double_fund_rejected()
    {
        var (svc, _, id) = Build();
        await svc.FundAsync(id, "approver@demo");
        // Trade is now AwaitingDocuments and IsFunded — a second fund must fail.
        await Assert.ThrowsAsync<TradeOperationException>(() => svc.FundAsync(id, "approver@demo"));
    }
}

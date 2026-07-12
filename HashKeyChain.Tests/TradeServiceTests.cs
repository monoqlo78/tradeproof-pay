using HashKeyChain.Data;
using HashKeyChain.Domain;
using HashKeyChain.Localization;
using HashKeyChain.Services.Trades;
using Microsoft.EntityFrameworkCore;

namespace HashKeyChain.Tests;

/// <summary>
/// Behavioural tests for the trade registration/approval flow (spec §5/§6):
/// reference generation, mandatory-field validation, the authoritative seller
/// wallet, condition locking after approval, and cancellation guards.
/// </summary>
public class TradeServiceTests
{
    private sealed class InMemoryFactory(string name) : IDbContextFactory<AppDbContext>
    {
        private readonly DbContextOptions<AppDbContext> _options =
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(name).Options;

        public AppDbContext CreateDbContext() => new(_options);
    }

    private static (ITradeService svc, IDbContextFactory<AppDbContext> factory) Build()
    {
        var factory = new InMemoryFactory($"trade-svc-{Guid.NewGuid():N}");
        var audit = new AuditWriter(factory);
        var svc = new TradeService(factory, new TradeStateMachine(), audit);
        return (svc, factory);
    }

    private static TradeConditionsInput ValidInput() => new()
    {
        BuyerCompanyId = 1,
        SellerCompanyId = 2,
        BuyerWalletAddress = "0xBUYER",
        SellerWalletAddress = "0xSELLER",
        PaymentAmount = 1000m,
        Currency = "USDC"
    };

    [Fact]
    public async Task CreateDraft_generates_reference_and_audits()
    {
        var (svc, factory) = Build();
        var trade = await svc.CreateDraftAsync(ValidInput(), "op@demo");

        Assert.StartsWith("TP-", trade.TradeReference);
        Assert.Equal(TradeStatus.Draft, trade.Status);
        Assert.Equal("0xSELLER", trade.SellerWalletAddress);

        await using var db = factory.CreateDbContext();
        Assert.True(await db.AuditEntries.AnyAsync(a => a.Action == AuditAction.TradeCreated));
    }

    [Fact]
    public async Task CreateDraft_rejects_missing_seller_wallet()
    {
        var (svc, _) = Build();
        var input = ValidInput();
        input.SellerWalletAddress = "";
        await Assert.ThrowsAsync<TradeOperationException>(() => svc.CreateDraftAsync(input, "op@demo"));
    }

    [Fact]
    public async Task CreateDraft_rejects_same_buyer_and_seller()
    {
        var (svc, _) = Build();
        var input = ValidInput();
        input.SellerCompanyId = input.BuyerCompanyId;
        await Assert.ThrowsAsync<TradeOperationException>(() => svc.CreateDraftAsync(input, "op@demo"));
    }

    [Fact]
    public async Task Approval_locks_conditions_and_blocks_further_edits()
    {
        var (svc, _) = Build();
        var trade = await svc.CreateDraftAsync(ValidInput(), "op@demo");
        await svc.RequestApprovalAsync(trade.Id, "op@demo");
        var approved = await svc.ApproveConditionsAsync(trade.Id, "approver@demo");

        Assert.True(approved.ConditionsLocked);
        Assert.Equal(TradeStatus.AwaitingFunding, approved.Status);

        var input = ValidInput();
        input.PaymentAmount = 5000m;
        await Assert.ThrowsAsync<TradeOperationException>(() => svc.UpdateConditionsAsync(trade.Id, input, "op@demo"));
    }

    [Fact]
    public async Task Update_changes_conditions_while_draft()
    {
        var (svc, _) = Build();
        var trade = await svc.CreateDraftAsync(ValidInput(), "op@demo");

        var input = ValidInput();
        input.PaymentAmount = 2500m;
        var updated = await svc.UpdateConditionsAsync(trade.Id, input, "op@demo");
        Assert.Equal(2500m, updated.PaymentAmount);
    }

    [Fact]
    public async Task Cancel_from_draft_succeeds()
    {
        var (svc, _) = Build();
        var trade = await svc.CreateDraftAsync(ValidInput(), "op@demo");
        var cancelled = await svc.CancelAsync(trade.Id, "op@demo", "changed mind");
        Assert.Equal(TradeStatus.Cancelled, cancelled.Status);
    }

    [Fact]
    public async Task Cancel_blocked_when_funded()
    {
        var (svc, factory) = Build();
        var trade = await svc.CreateDraftAsync(ValidInput(), "op@demo");
        await using (var db = factory.CreateDbContext())
        {
            var t = await db.Trades.FirstAsync(x => x.Id == trade.Id);
            t.IsFunded = true;
            await db.SaveChangesAsync();
        }
        await Assert.ThrowsAsync<TradeOperationException>(() => svc.CancelAsync(trade.Id, "op@demo"));
    }
}

using System.Text;
using System.Text.Json;
using HashKeyChain.Data;
using HashKeyChain.Domain;
using HashKeyChain.Localization;
using HashKeyChain.Services.Analysis;
using HashKeyChain.Services.Documents;
using HashKeyChain.Services.Trades;
using HashKeyChain.Services.Verification;
using Microsoft.EntityFrameworkCore;

namespace HashKeyChain.Tests;

/// <summary>
/// End-to-end tests over document submission → analysis → verdict → verifier
/// confirmation (spec §8–§13), using the real analyzer and rule engine with an
/// in-memory database and storage.
/// </summary>
public class VerificationServiceTests
{
    private sealed class InMemoryFactory(string name) : IDbContextFactory<AppDbContext>
    {
        private readonly DbContextOptions<AppDbContext> _options =
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(name).Options;
        public AppDbContext CreateDbContext() => new(_options);
    }

    private sealed class InMemoryStorage : IDocumentStorage
    {
        private readonly Dictionary<string, byte[]> _files = new();
        public Task<string> SaveAsync(int tradeId, string relativeName, byte[] content, CancellationToken ct = default)
        {
            var path = $"trade-{tradeId}/{Guid.NewGuid():N}-{Path.GetFileName(relativeName)}";
            _files[path] = content;
            return Task.FromResult(path);
        }
        public Task<byte[]?> ReadAsync(string storagePath, CancellationToken ct = default) =>
            Task.FromResult(_files.TryGetValue(storagePath, out var b) ? b : null);
    }

    private static int _nextId = 900_000;
    private static int UniqueId() => Interlocked.Increment(ref _nextId);

    private sealed record Harness(
        IDocumentService Docs, IVerificationService Verify, IDbContextFactory<AppDbContext> Factory, int TradeId);

    private static Harness Build()
    {
        var factory = new InMemoryFactory($"verif-{Guid.NewGuid():N}");
        var storage = new InMemoryStorage();
        var audit = new AuditWriter(factory);
        var sm = new TradeStateMachine();
        var docs = new DocumentService(factory, storage, sm, audit);
        var verify = new VerificationService(factory, sm, storage,
            new MockDocumentAnalysisService(), new TradeRuleEngine(), audit);

        var id = UniqueId();
        using (var db = factory.CreateDbContext())
        {
            db.Companies.Add(new Company { Id = id * 10 + 1, Name = "Buyer Co" });
            db.Companies.Add(new Company { Id = id * 10 + 2, Name = "Seller Co" });
            db.Trades.Add(new Trade
            {
                Id = id,
                TradeReference = $"T-{id}",
                Status = TradeStatus.AwaitingDocuments,
                TransportMode = TransportMode.Sea,
                BuyerCompanyId = id * 10 + 1,
                SellerCompanyId = id * 10 + 2,
                BuyerWalletAddress = "0xB",
                SellerWalletAddress = "0xSELLER",
                PaymentAmount = 1000m,
                Currency = "USDC",
                ExpectedQuantity = 100m,
                LatestShipmentDate = new DateTime(2026, 8, 1),
                ConditionsLocked = true,
                IsFunded = true
            });
            db.SaveChanges();
        }
        return new Harness(docs, verify, factory, id);
    }

    private static byte[] Clean() => Encoding.UTF8.GetBytes("scanned document");

    private static async Task UploadAllClean(Harness h)
    {
        await h.Docs.UploadAsync(h.TradeId, DocumentType.CommercialInvoice, "i.pdf", Clean(), "seller@demo");
        await h.Docs.UploadAsync(h.TradeId, DocumentType.PackingList, "p.pdf", Clean(), "seller@demo");
        await h.Docs.UploadAsync(h.TradeId, DocumentType.BillOfLading, "b.pdf", Clean(), "seller@demo");
    }

    [Fact]
    public async Task Clean_documents_pass_then_verifier_confirms()
    {
        var h = Build();
        await UploadAllClean(h);

        var run = await h.Verify.RunAnalysisAsync(h.TradeId, "verifier@demo");
        Assert.Equal(VerificationResult.Pass, run.Result);

        await using (var db = h.Factory.CreateDbContext())
        {
            var trade = await db.Trades.FirstAsync(t => t.Id == h.TradeId);
            Assert.Equal(TradeStatus.ReadyForVerification, trade.Status);
        }

        var confirmed = await h.Verify.ConfirmAsync(h.TradeId, "verifier@demo");
        Assert.Equal(TradeStatus.ReadyForApproval, confirmed.Status);
        Assert.Equal(VerificationResult.ReadyForApproval, confirmed.LatestVerdict);
    }

    [Fact]
    public async Task Amount_mismatch_blocks()
    {
        var h = Build();
        // Inject a bad invoice via demo JSON payload.
        var badInvoice = JsonSerializer.Serialize(new { fields = new { totalAmount = 1m, currency = "USDC", sellerWalletAddress = "0xSELLER" } });
        await h.Docs.UploadAsync(h.TradeId, DocumentType.CommercialInvoice, "i.json", Encoding.UTF8.GetBytes(badInvoice), "seller@demo");
        await h.Docs.UploadAsync(h.TradeId, DocumentType.PackingList, "p.pdf", Clean(), "seller@demo");
        await h.Docs.UploadAsync(h.TradeId, DocumentType.BillOfLading, "b.pdf", Clean(), "seller@demo");

        var run = await h.Verify.RunAnalysisAsync(h.TradeId, "verifier@demo");
        Assert.Equal(VerificationResult.Blocked, run.Result);

        await Assert.ThrowsAsync<TradeOperationException>(() => h.Verify.ConfirmAsync(h.TradeId, "verifier@demo"));
    }

    [Fact]
    public async Task Low_confidence_routes_to_manual_review_then_confirm()
    {
        var h = Build();
        var lowConf = JsonSerializer.Serialize(new { confidence = 0.4, fields = new { totalAmount = 1000m, currency = "USDC", sellerWalletAddress = "0xSELLER" } });
        await h.Docs.UploadAsync(h.TradeId, DocumentType.CommercialInvoice, "i.json", Encoding.UTF8.GetBytes(lowConf), "seller@demo");
        await h.Docs.UploadAsync(h.TradeId, DocumentType.PackingList, "p.pdf", Clean(), "seller@demo");
        await h.Docs.UploadAsync(h.TradeId, DocumentType.BillOfLading, "b.pdf", Clean(), "seller@demo");

        var run = await h.Verify.RunAnalysisAsync(h.TradeId, "verifier@demo");
        Assert.Equal(VerificationResult.ManualReview, run.Result);

        var confirmed = await h.Verify.ConfirmAsync(h.TradeId, "verifier@demo", "reviewed manually");
        Assert.Equal(TradeStatus.ReadyForApproval, confirmed.Status);
    }

    [Fact]
    public async Task Reject_sends_back_to_awaiting_documents_after_resubmit()
    {
        var h = Build();
        await UploadAllClean(h);
        await h.Verify.RunAnalysisAsync(h.TradeId, "verifier@demo");

        var rejected = await h.Verify.RejectAsync(h.TradeId, "verifier@demo", "smudged scan");
        Assert.Equal(TradeStatus.DocumentsRejected, rejected.Status);

        // Seller resubmits and re-runs analysis.
        await h.Docs.UploadAsync(h.TradeId, DocumentType.CommercialInvoice, "i2.pdf", Clean(), "seller@demo");
        var run2 = await h.Verify.RunAnalysisAsync(h.TradeId, "verifier@demo");
        Assert.Equal(VerificationResult.Pass, run2.Result);
    }
}

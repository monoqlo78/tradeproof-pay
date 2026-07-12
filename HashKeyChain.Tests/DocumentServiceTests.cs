using System.Text;
using HashKeyChain.Data;
using HashKeyChain.Domain;
using HashKeyChain.Localization;
using HashKeyChain.Services.Documents;
using HashKeyChain.Services.Trades;
using Microsoft.EntityFrameworkCore;

namespace HashKeyChain.Tests;

/// <summary>
/// Tests for document submission (spec §8): SHA-256 hashing, version supersession
/// (prior versions retained but not current), required-document tracking by
/// transport mode, and state guards.
/// </summary>
public class DocumentServiceTests
{
    private sealed class InMemoryFactory(string name) : IDbContextFactory<AppDbContext>
    {
        private readonly DbContextOptions<AppDbContext> _options =
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(name).Options;

        public AppDbContext CreateDbContext() => new(_options);
    }

    private sealed class InMemoryStorage : IDocumentStorage
    {
        public readonly Dictionary<string, byte[]> Files = new();

        public Task<string> SaveAsync(int tradeId, string relativeName, byte[] content, CancellationToken ct = default)
        {
            var path = $"trade-{tradeId}/{Path.GetFileName(relativeName)}";
            Files[path] = content;
            return Task.FromResult(path);
        }

        public Task<byte[]?> ReadAsync(string storagePath, CancellationToken ct = default) =>
            Task.FromResult(Files.TryGetValue(storagePath, out var b) ? b : null);
    }

    private static int _nextId = 500_000;
    private static int UniqueId() => Interlocked.Increment(ref _nextId);

    private static (IDocumentService svc, IDbContextFactory<AppDbContext> factory, int tradeId) Build(
        TransportMode mode = TransportMode.Sea, TradeStatus status = TradeStatus.AwaitingDocuments)
    {
        var factory = new InMemoryFactory($"docsvc-{Guid.NewGuid():N}");
        var svc = new DocumentService(factory, new InMemoryStorage(), new TradeStateMachine(), new AuditWriter(factory));

        var id = UniqueId();
        using var db = factory.CreateDbContext();
        db.Trades.Add(new Trade
        {
            Id = id,
            TradeReference = $"T-{id}",
            Status = status,
            TransportMode = mode,
            BuyerWalletAddress = "0xB",
            SellerWalletAddress = "0xS",
            PaymentAmount = 1m
        });
        db.SaveChanges();
        return (svc, factory, id);
    }

    private static byte[] Bytes(string s) => Encoding.UTF8.GetBytes(s);

    [Fact]
    public async Task Upload_computes_sha256_and_sets_current()
    {
        var (svc, _, id) = Build();
        var v = await svc.UploadAsync(id, DocumentType.CommercialInvoice, "invoice.pdf", Bytes("hello"), "seller@demo");

        // SHA-256 of "hello".
        Assert.Equal("2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824", v.Sha256);
        Assert.Equal(1, v.Version);
        Assert.True(v.IsCurrent);
    }

    [Fact]
    public async Task Reupload_supersedes_prior_version()
    {
        var (svc, factory, id) = Build();
        await svc.UploadAsync(id, DocumentType.CommercialInvoice, "v1.pdf", Bytes("one"), "seller@demo");
        var v2 = await svc.UploadAsync(id, DocumentType.CommercialInvoice, "v2.pdf", Bytes("two"), "seller@demo");

        Assert.Equal(2, v2.Version);

        await using var db = factory.CreateDbContext();
        var versions = await db.DocumentVersions.Where(x => x.TradeDocument!.TradeId == id).ToListAsync();
        Assert.Equal(2, versions.Count);
        Assert.Single(versions.Where(x => x.IsCurrent));
        Assert.Equal(2, versions.Single(x => x.IsCurrent).Version);
    }

    [Fact]
    public async Task Missing_required_reflects_sea_mode()
    {
        var (svc, _, id) = Build(TransportMode.Sea);
        var missing0 = await svc.MissingRequiredAsync(id);
        Assert.Contains(DocumentType.BillOfLading, missing0);
        Assert.DoesNotContain(DocumentType.AirWaybill, missing0);

        await svc.UploadAsync(id, DocumentType.CommercialInvoice, "i.pdf", Bytes("i"), "seller@demo");
        await svc.UploadAsync(id, DocumentType.PackingList, "p.pdf", Bytes("p"), "seller@demo");
        await svc.UploadAsync(id, DocumentType.BillOfLading, "b.pdf", Bytes("b"), "seller@demo");

        Assert.True(await svc.HasAllRequiredAsync(id));
    }

    [Fact]
    public async Task Air_mode_requires_air_waybill()
    {
        var (svc, _, id) = Build(TransportMode.Air);
        var missing = await svc.MissingRequiredAsync(id);
        Assert.Contains(DocumentType.AirWaybill, missing);
        Assert.DoesNotContain(DocumentType.BillOfLading, missing);
    }

    [Fact]
    public async Task Upload_rejected_in_wrong_state()
    {
        var (svc, _, id) = Build(status: TradeStatus.Draft);
        await Assert.ThrowsAsync<TradeOperationException>(
            () => svc.UploadAsync(id, DocumentType.CommercialInvoice, "i.pdf", Bytes("i"), "seller@demo"));
    }

    [Fact]
    public async Task Empty_file_rejected()
    {
        var (svc, _, id) = Build();
        await Assert.ThrowsAsync<TradeOperationException>(
            () => svc.UploadAsync(id, DocumentType.CommercialInvoice, "i.pdf", Array.Empty<byte>(), "seller@demo"));
    }
}

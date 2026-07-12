using System.Text;
using System.Text.Json;
using HashKeyChain.Data;
using HashKeyChain.Domain;
using HashKeyChain.Localization;
using HashKeyChain.Services.Documents;
using HashKeyChain.Services.Settlement;
using HashKeyChain.Services.Trades;
using HashKeyChain.Services.Verification;
using Microsoft.EntityFrameworkCore;

namespace HashKeyChain.Services.Demo;

/// <summary>
/// Drives complete trade lifecycles end-to-end through the real services and
/// database, producing a spread of demo trades in interesting terminal/mid states
/// (Settled, Blocked, ManualReview→Settled, Refunded). This is both a live
/// end-to-end verification against the configured database and a way to populate
/// the dashboard for a demo. It only runs on explicit request (the
/// <c>--run-demo-flow</c> CLI switch) and is idempotent: a scenario whose marked
/// trade already exists is skipped so re-runs never pile up data. All writes stay
/// within the hashkeychain schema and never touch existing/other data.
/// </summary>
public interface IDemoScenarioRunner
{
    Task<IReadOnlyList<string>> RunAsync(CancellationToken ct = default);
}

public sealed class DemoScenarioRunner(
    IDbContextFactory<AppDbContext> factory,
    ITradeService trades,
    IEscrowService escrow,
    IDocumentService documents,
    IVerificationService verification,
    ISettlementService settlement,
    IRefundService refund,
    ILogger<DemoScenarioRunner> logger) : IDemoScenarioRunner
{
    private const string Operator = "operator@tradeproof.demo";
    private const string Approver = "approver@tradeproof.demo";
    private const string Seller = "seller@tradeproof.demo";
    private const string Verifier = "verifier@tradeproof.demo";

    private static readonly byte[] CleanDoc = Encoding.UTF8.GetBytes("CLEAN DEMO DOCUMENT — fields derived from trade.");

    public async Task<IReadOnlyList<string>> RunAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var buyer = await db.Companies.FirstOrDefaultAsync(c => c.Name == DemoDataSeeder.BuyerCompanyName, ct)
                    ?? throw new InvalidOperationException("Demo buyer company not seeded. Run the app once to seed, then retry.");
        var seller = await db.Companies.FirstOrDefaultAsync(c => c.Name == DemoDataSeeder.SellerCompanyName, ct)
                     ?? throw new InvalidOperationException("Demo seller company not seeded.");

        var results = new List<string>();
        results.Add(await RunScenarioAsync("clean-settled", "クリーン → 決済完了", buyer, seller, RunCleanSettledAsync, ct));
        results.Add(await RunScenarioAsync("mismatch-blocked", "金額不一致 → ブロック", buyer, seller, RunMismatchBlockedAsync, ct));
        results.Add(await RunScenarioAsync("review-settled", "低信頼度 → 人手確認 → 決済", buyer, seller, RunManualReviewSettledAsync, ct));
        results.Add(await RunScenarioAsync("funded-refunded", "入金後 → 期限切れ → 返金", buyer, seller, RunFundedRefundedAsync, ct));
        return results;
    }

    private async Task<string> RunScenarioAsync(
        string key, string title, Company buyer, Company seller,
        Func<string, Company, Company, CancellationToken, Task<string>> body, CancellationToken ct)
    {
        var marker = $"[demo-scenario:{key}]";
        await using var db = await factory.CreateDbContextAsync(ct);
        var existing = await db.Trades.FirstOrDefaultAsync(t => t.Notes != null && t.Notes.Contains(marker), ct);
        if (existing is not null)
            return $"SKIP  {key,-18} 既存 {existing.TradeReference} ({existing.Status})";

        try
        {
            var outcome = await body(marker, buyer, seller, ct);
            logger.LogInformation("Demo scenario {Key} → {Outcome}", key, outcome);
            return $"OK    {key,-18} {title} → {outcome}";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Demo scenario {Key} failed", key);
            return $"FAIL  {key,-18} {title} → {ex.Message}";
        }
    }

    private TradeConditionsInput NewInput(string marker, Company buyer, Company seller) => new()
    {
        BuyerCompanyId = buyer.Id,
        SellerCompanyId = seller.Id,
        BuyerWalletAddress = buyer.WalletAddress ?? "0xB0B0000000000000000000000000000000000001",
        SellerWalletAddress = seller.WalletAddress ?? "0x5E11E20000000000000000000000000000000002",
        TransportMode = TransportMode.Sea,
        PaymentToken = "MockUSDC",
        PaymentAmount = 125_000m,
        Currency = "USDC",
        PurchaseOrderNumber = "PO-DEMO-001",
        ExpectedProductDescription = "Coffee beans, grade A",
        ExpectedQuantity = 100m,
        LatestShipmentDate = DateTime.UtcNow.Date.AddDays(30),
        DocumentSubmissionDeadline = DateTime.UtcNow.Date.AddDays(20),
        PaymentExpiry = DateTime.UtcNow.Date.AddDays(30),
        Notes = $"デモシナリオ用取引。{marker}"
    };

    /// <summary>Draft → approve conditions → fund escrow → AwaitingDocuments.</summary>
    private async Task<Trade> DraftFundAsync(string marker, Company buyer, Company seller, CancellationToken ct)
    {
        var trade = await trades.CreateDraftAsync(NewInput(marker, buyer, seller), Operator, ct);
        await trades.RequestApprovalAsync(trade.Id, Operator, ct);
        await trades.ApproveConditionsAsync(trade.Id, Approver, ct: ct);
        await escrow.FundAsync(trade.Id, Approver, ct);
        return trade;
    }

    private async Task UploadTransportSetAsync(int tradeId, byte[]? invoiceOverride, CancellationToken ct)
    {
        await documents.UploadAsync(tradeId, DocumentType.CommercialInvoice, "invoice.txt",
            invoiceOverride ?? CleanDoc, Seller, ct);
        await documents.UploadAsync(tradeId, DocumentType.PackingList, "packing.txt", CleanDoc, Seller, ct);
        await documents.UploadAsync(tradeId, DocumentType.BillOfLading, "bol.txt", CleanDoc, Seller, ct);
    }

    private async Task<string> RunCleanSettledAsync(string marker, Company buyer, Company seller, CancellationToken ct)
    {
        var trade = await DraftFundAsync(marker, buyer, seller, ct);
        await UploadTransportSetAsync(trade.Id, invoiceOverride: null, ct);
        var run = await verification.RunAnalysisAsync(trade.Id, Verifier, ct);
        if (run.Result != VerificationResult.Pass)
            throw new InvalidOperationException($"expected Pass, got {run.Result}");
        await verification.ConfirmAsync(trade.Id, Verifier, ct: ct);
        await settlement.ApprovePaymentAsync(trade.Id, Approver, ct: ct);
        var settled = await settlement.SettleAsync(trade.Id, Approver, ct);
        return $"{settled.TradeReference} {settled.Status} tx={settled.SettlementTxHash?[..10]}…";
    }

    private async Task<string> RunMismatchBlockedAsync(string marker, Company buyer, Company seller, CancellationToken ct)
    {
        var trade = await DraftFundAsync(marker, buyer, seller, ct);
        // Invoice claims a wrong total → hard-rule failure → Blocked.
        var badInvoice = Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new { confidence = 0.95, fields = new { totalAmount = 1, currency = "USDC" } }));
        await UploadTransportSetAsync(trade.Id, invoiceOverride: badInvoice, ct);
        var run = await verification.RunAnalysisAsync(trade.Id, Verifier, ct);
        if (run.Result != VerificationResult.Blocked)
            throw new InvalidOperationException($"expected Blocked, got {run.Result}");
        return $"{trade.TradeReference} Blocked ({run.Summary})";
    }

    private async Task<string> RunManualReviewSettledAsync(string marker, Company buyer, Company seller, CancellationToken ct)
    {
        var trade = await DraftFundAsync(marker, buyer, seller, ct);
        // Invoice with low confidence but clean fields → soft failure → ManualReview.
        var lowConf = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { confidence = 0.5 }));
        await UploadTransportSetAsync(trade.Id, invoiceOverride: lowConf, ct);
        var run = await verification.RunAnalysisAsync(trade.Id, Verifier, ct);
        if (run.Result != VerificationResult.ManualReview)
            throw new InvalidOperationException($"expected ManualReview, got {run.Result}");
        // Human verifier reviews and confirms → ReadyForApproval.
        await verification.ConfirmAsync(trade.Id, Verifier, "低信頼度を人手で確認し問題なしと判断", ct);
        await settlement.ApprovePaymentAsync(trade.Id, Approver, ct: ct);
        var settled = await settlement.SettleAsync(trade.Id, Approver, ct);
        return $"{settled.TradeReference} {settled.Status} (ManualReview 経由)";
    }

    private async Task<string> RunFundedRefundedAsync(string marker, Company buyer, Company seller, CancellationToken ct)
    {
        var trade = await DraftFundAsync(marker, buyer, seller, ct);
        // Simulate the payment window elapsing without settlement, then refund.
        await refund.MarkExpiredAsync(trade.Id, Approver, force: true, ct);
        var refunded = await refund.RefundAsync(trade.Id, Approver, ct);
        return $"{refunded.TradeReference} {refunded.Status} tx={refunded.RefundTxHash?[..10]}…";
    }
}

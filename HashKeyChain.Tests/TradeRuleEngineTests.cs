using HashKeyChain.Domain;
using HashKeyChain.Localization;
using HashKeyChain.Services.Analysis;

namespace HashKeyChain.Tests;

/// <summary>
/// Tests for the deterministic rule engine (spec §10/§11): clean documents pass,
/// hard mismatches block, soft issues route to manual review, and a low AI risk
/// level never rescues a hard failure.
/// </summary>
public class TradeRuleEngineTests
{
    private readonly ITradeRuleEngine _engine = new TradeRuleEngine();

    private static Trade SeaTrade() => new()
    {
        Id = 1,
        TransportMode = TransportMode.Sea,
        PaymentAmount = 1000m,
        Currency = "USDC",
        SellerWalletAddress = "0xSELLER",
        ExpectedQuantity = 100m,
        LatestShipmentDate = new DateTime(2026, 8, 1)
    };

    private static AnalyzedDocument Invoice(decimal amount = 1000m, string currency = "USDC",
        string? wallet = "0xSELLER", double confidence = 0.95) =>
        new(DocumentType.CommercialInvoice, DocumentType.CommercialInvoice, confidence,
            new ExtractedDocumentFields { TotalAmount = amount, Currency = currency, SellerWalletAddress = wallet });

    private static AnalyzedDocument Packing(decimal qty = 100m, string container = "C-1", double confidence = 0.95) =>
        new(DocumentType.PackingList, DocumentType.PackingList, confidence,
            new ExtractedDocumentFields { Quantity = qty, ContainerNumber = container });

    private static AnalyzedDocument Bol(string container = "C-1", DateTime? shipped = null, double confidence = 0.95) =>
        new(DocumentType.BillOfLading, DocumentType.BillOfLading, confidence,
            new ExtractedDocumentFields { ContainerNumber = container, ShipmentDate = shipped ?? new DateTime(2026, 7, 20) });

    private RuleContext Ctx(params AnalyzedDocument[] docs) =>
        new(SeaTrade(), "Buyer Co", "Seller Co", docs);

    [Fact]
    public void Clean_documents_pass()
    {
        var run = _engine.Evaluate(Ctx(Invoice(), Packing(), Bol()));
        Assert.Equal(VerificationResult.Pass, run.Result);
        Assert.All(run.Checks, c => Assert.True(c.Passed));
    }

    [Fact]
    public void Amount_mismatch_blocks()
    {
        var run = _engine.Evaluate(Ctx(Invoice(amount: 999m), Packing(), Bol()));
        Assert.Equal(VerificationResult.Blocked, run.Result);
        Assert.Contains(run.Checks, c => c.RuleKey == "Invoice_TotalAmount_MatchesTrade" && !c.Passed);
    }

    [Fact]
    public void Altered_seller_wallet_blocks()
    {
        var run = _engine.Evaluate(Ctx(Invoice(wallet: "0xATTACKER"), Packing(), Bol()));
        Assert.Equal(VerificationResult.Blocked, run.Result);
        Assert.Contains(run.Checks, c => c.RuleKey == "SellerWallet_NotAltered" && !c.Passed);
    }

    [Fact]
    public void Quantity_mismatch_blocks()
    {
        var run = _engine.Evaluate(Ctx(Invoice(), Packing(qty: 50m), Bol()));
        Assert.Equal(VerificationResult.Blocked, run.Result);
    }

    [Fact]
    public void Container_inconsistency_blocks()
    {
        var run = _engine.Evaluate(Ctx(Invoice(), Packing(container: "C-1"), Bol(container: "C-2")));
        Assert.Equal(VerificationResult.Blocked, run.Result);
        Assert.Contains(run.Checks, c => c.RuleKey == "Container_Consistency" && !c.Passed);
    }

    [Fact]
    public void Late_shipment_blocks()
    {
        var run = _engine.Evaluate(Ctx(Invoice(), Packing(), Bol(shipped: new DateTime(2026, 9, 1))));
        Assert.Equal(VerificationResult.Blocked, run.Result);
    }

    [Fact]
    public void Missing_required_document_blocks()
    {
        var run = _engine.Evaluate(Ctx(Invoice(), Packing()));
        Assert.Equal(VerificationResult.Blocked, run.Result);
        Assert.Contains(run.Checks, c => c.RuleKey == "Required_BillOfLading" && !c.Passed);
    }

    [Fact]
    public void Low_confidence_routes_to_manual_review()
    {
        var run = _engine.Evaluate(Ctx(Invoice(confidence: 0.5), Packing(), Bol()));
        Assert.Equal(VerificationResult.ManualReview, run.Result);
        Assert.Contains(run.Checks, c => c.RuleKey.StartsWith("Confidence_") && !c.Passed);
    }

    [Fact]
    public void Hard_failure_beats_low_risk()
    {
        // High confidence (low risk) but a hard amount mismatch must still block.
        var run = _engine.Evaluate(Ctx(Invoice(amount: 1m, confidence: 0.99), Packing(), Bol()));
        Assert.Equal(VerificationResult.Blocked, run.Result);
    }
}

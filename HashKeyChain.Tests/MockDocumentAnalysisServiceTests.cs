using System.Text;
using System.Text.Json;
using HashKeyChain.Domain;
using HashKeyChain.Localization;
using HashKeyChain.Services.Analysis;

namespace HashKeyChain.Tests;

/// <summary>
/// Tests for the DemoMode analyzer: it derives clean fields from the trade for
/// arbitrary content, and honours an injected JSON payload (used by demo
/// scenarios to force mismatches).
/// </summary>
public class MockDocumentAnalysisServiceTests
{
    private readonly IDocumentAnalysisService _svc = new MockDocumentAnalysisService();

    private static Trade Trade() => new()
    {
        Id = 7,
        TransportMode = TransportMode.Sea,
        PaymentAmount = 1000m,
        Currency = "USDC",
        SellerWalletAddress = "0xSELLER",
        ExpectedQuantity = 100m
    };

    [Fact]
    public async Task Arbitrary_content_yields_clean_invoice_fields()
    {
        var result = await _svc.AnalyzeAsync(DocumentType.CommercialInvoice, "x.pdf",
            Encoding.UTF8.GetBytes("not json"), Trade());

        Assert.Equal(1000m, result.Fields.TotalAmount);
        Assert.Equal("USDC", result.Fields.Currency);
        Assert.Equal("0xSELLER", result.Fields.SellerWalletAddress);
        Assert.True(result.Confidence >= 0.75);
        Assert.Equal(DocumentType.CommercialInvoice, result.DetectedType);
    }

    [Fact]
    public async Task Json_payload_overrides_fields()
    {
        var payload = JsonSerializer.Serialize(new
        {
            confidence = 0.4,
            detectedType = "PackingList",
            fields = new { totalAmount = 5m, currency = "JPY" }
        });

        var result = await _svc.AnalyzeAsync(DocumentType.CommercialInvoice, "x.json",
            Encoding.UTF8.GetBytes(payload), Trade());

        Assert.Equal(5m, result.Fields.TotalAmount);
        Assert.Equal("JPY", result.Fields.Currency);
        Assert.Equal(0.4, result.Confidence);
        Assert.Equal(DocumentType.PackingList, result.DetectedType);
    }
}

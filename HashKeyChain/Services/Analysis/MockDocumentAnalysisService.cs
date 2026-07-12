using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using HashKeyChain.Domain;
using HashKeyChain.Localization;

namespace HashKeyChain.Services.Analysis;

/// <summary>
/// Deterministic DemoMode analyzer. If the uploaded content is a JSON payload in
/// the demo schema, its fields are used verbatim — this is how demo scenarios
/// inject either clean documents or deliberate mismatches (spec §25). Otherwise
/// the analyzer derives "clean" fields from the trade so an arbitrary upload
/// passes verification. No external service is called.
/// </summary>
public sealed class MockDocumentAnalysisService : IDocumentAnalysisService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public bool IsMock => true;

    public Task<DocumentAnalysisResult> AnalyzeAsync(
        DocumentType declaredType, string fileName, byte[] content, Trade trade, CancellationToken ct = default)
    {
        if (TryParseDemoPayload(content, out var payload) && payload is not null)
        {
            var fields = payload.Fields ?? DeriveFields(declaredType, trade);
            return Task.FromResult(new DocumentAnalysisResult(
                fields,
                payload.Confidence ?? 0.95,
                payload.DetectedType ?? declaredType,
                payload.SourceLanguage ?? "en",
                payload.RiskLevel ?? RiskFromConfidence(payload.Confidence ?? 0.95)));
        }

        var derived = DeriveFields(declaredType, trade);
        return Task.FromResult(new DocumentAnalysisResult(derived, 0.95, declaredType, "en", "Low"));
    }

    private static bool TryParseDemoPayload(byte[] content, out DemoAnalysisPayload? payload)
    {
        payload = null;
        try
        {
            var text = Encoding.UTF8.GetString(content).TrimStart();
            if (text.Length == 0 || text[0] != '{')
                return false;
            payload = JsonSerializer.Deserialize<DemoAnalysisPayload>(text, JsonOptions);
            return payload is not null;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static ExtractedDocumentFields DeriveFields(DocumentType type, Trade trade)
    {
        var container = $"DEMO-CU-{trade.Id:D6}";
        return type switch
        {
            DocumentType.CommercialInvoice => new ExtractedDocumentFields
            {
                TotalAmount = trade.PaymentAmount,
                Currency = trade.Currency,
                Quantity = trade.ExpectedQuantity,
                ProductDescription = trade.ExpectedProductDescription,
                SellerWalletAddress = trade.SellerWalletAddress,
                PurchaseOrderNumber = trade.PurchaseOrderNumber
            },
            DocumentType.PackingList => new ExtractedDocumentFields
            {
                Quantity = trade.ExpectedQuantity,
                ProductDescription = trade.ExpectedProductDescription,
                ContainerNumber = container
            },
            DocumentType.BillOfLading or DocumentType.AirWaybill => new ExtractedDocumentFields
            {
                ContainerNumber = container,
                ShipmentDate = trade.LatestShipmentDate ?? DateTime.UtcNow.Date
            },
            _ => new ExtractedDocumentFields()
        };
    }

    private static string RiskFromConfidence(double confidence) =>
        confidence >= 0.85 ? "Low" : confidence >= 0.6 ? "Medium" : "High";

    private sealed class DemoAnalysisPayload
    {
        public double? Confidence { get; set; }
        public DocumentType? DetectedType { get; set; }
        public string? SourceLanguage { get; set; }
        public string? RiskLevel { get; set; }
        public ExtractedDocumentFields? Fields { get; set; }
    }
}

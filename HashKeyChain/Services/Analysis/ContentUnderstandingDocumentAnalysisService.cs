using System.Text;
using HashKeyChain.Domain;
using HashKeyChain.Localization;

namespace HashKeyChain.Services.Analysis;

/// <summary>
/// Real document analysis via Azure AI Content Understanding. Runs the received
/// shipping document (Bill of Lading, Air Waybill, Certificate of Origin,
/// Commercial Invoice, Packing List, ...) through the same custom analyzer used for
/// contract auto-fill, then maps the extracted values onto
/// <see cref="ExtractedDocumentFields"/> so the deterministic rule engine can
/// cross-check them against the locked trade conditions (quantity, weight, goods,
/// amount) — the documentary check a bank performs.
///
/// Safety: demo JSON payloads and ANY failure fall back to the deterministic mock,
/// so DemoMode never breaks even when Content Understanding is unavailable.
/// </summary>
public sealed class ContentUnderstandingDocumentAnalysisService : IDocumentAnalysisService
{
    private readonly ContentUnderstandingClient _client;
    private readonly MockDocumentAnalysisService _fallback;
    private readonly ILogger<ContentUnderstandingDocumentAnalysisService> _logger;

    public ContentUnderstandingDocumentAnalysisService(
        ContentUnderstandingClient client,
        MockDocumentAnalysisService fallback,
        ILogger<ContentUnderstandingDocumentAnalysisService> logger)
    {
        _client = client;
        _fallback = fallback;
        _logger = logger;
    }

    public bool IsMock => false;

    public async Task<DocumentAnalysisResult> AnalyzeAsync(
        DocumentType declaredType, string fileName, byte[] content, Trade trade, CancellationToken ct = default)
    {
        // Demo JSON payloads (scenario runner) bypass OCR and use the deterministic mock.
        if (LooksLikeJson(content) || !_client.IsConfigured)
            return await _fallback.AnalyzeAsync(declaredType, fileName, content, trade, ct);

        try
        {
            var fields = await _client.AnalyzeAsync(content, ct);
            if (fields is null || fields.Count == 0)
                return await _fallback.AnalyzeAsync(declaredType, fileName, content, trade, ct);

            var extracted = new ExtractedDocumentFields
            {
                TotalAmount = Num(fields, "TotalAmount"),
                Currency = Get(fields, "Currency"),
                Quantity = Num(fields, "Quantity"),
                ProductDescription = Get(fields, "ProductDescription"),
                SellerName = Get(fields, "SellerName"),
                BuyerName = Get(fields, "BuyerName"),
                SellerWalletAddress = Get(fields, "SellerWalletAddress"),
                ContainerNumber = Get(fields, "ContainerNumber"),
                ShipmentDate = Date(fields, "LatestShipmentDate"),
                Incoterms = Get(fields, "Incoterms"),
                PurchaseOrderNumber = Get(fields, "PurchaseOrderNumber"),
            };

            var detected = MapDocumentType(Get(fields, "DocumentType")) ?? declaredType;

            _logger.LogInformation(
                "Content Understanding analysis for {File} ({Declared}) detected {Detected}.",
                fileName, declaredType, detected);

            return new DocumentAnalysisResult(extracted, 0.92, detected, "en", "Low");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Content Understanding analysis failed for {File}; falling back to mock.", fileName);
            return await _fallback.AnalyzeAsync(declaredType, fileName, content, trade, ct);
        }
    }

    private static DocumentType? MapDocumentType(string? raw)
    {
        var s = (raw ?? string.Empty).Replace(" ", "").ToLowerInvariant();
        return s switch
        {
            "commercialinvoice" or "invoice" => DocumentType.CommercialInvoice,
            "packinglist" => DocumentType.PackingList,
            "billoflading" or "bl" => DocumentType.BillOfLading,
            "airwaybill" or "awb" => DocumentType.AirWaybill,
            "airtransferrelease" => DocumentType.AirTransferRelease,
            "certificateoforigin" or "certoforigin" => DocumentType.CertificateOfOrigin,
            _ => null
        };
    }

    private static bool LooksLikeJson(byte[] content)
    {
        if (content.Length == 0)
            return false;
        var text = Encoding.UTF8.GetString(content, 0, Math.Min(content.Length, 64)).TrimStart();
        return text.Length > 0 && text[0] == '{';
    }

    private static string? Get(IReadOnlyDictionary<string, CuFieldValue> f, string key) =>
        f.TryGetValue(key, out var v) ? v.Text : null;

    private static decimal? Num(IReadOnlyDictionary<string, CuFieldValue> f, string key) =>
        f.TryGetValue(key, out var v) ? v.AsNumber : null;

    private static DateTime? Date(IReadOnlyDictionary<string, CuFieldValue> f, string key) =>
        f.TryGetValue(key, out var v) ? v.AsDate : null;
}

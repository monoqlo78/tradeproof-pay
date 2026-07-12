using System.Text;
using System.Text.Json;
using Azure;
using Azure.AI.DocumentIntelligence;
using HashKeyChain.Configuration;
using HashKeyChain.Domain;
using HashKeyChain.Localization;
using HashKeyChain.Services.Agent;
using Microsoft.Extensions.Options;
using OpenAI.Chat;

namespace HashKeyChain.Services.Analysis;

/// <summary>
/// Real OCR / document-understanding via Azure AI Document Intelligence
/// (prebuilt-layout) followed by an LLM normalization pass (gpt-5.4) that maps the
/// recognized content to <see cref="ExtractedDocumentFields"/>. The normalization
/// prompt is intentionally NOT given the trade's expected values, so the extracted
/// fields stay faithful to the document and the deterministic rule engine can
/// still detect genuine mismatches.
///
/// Safety: a demo JSON payload (used by the scenario runner) is passed straight to
/// the mock analyzer, and ANY failure falls back to the mock — so DemoMode never
/// breaks even if the AI services are unavailable.
/// </summary>
public sealed class AzureDocumentIntelligenceAnalysisService : IDocumentAnalysisService
{
    private readonly DocumentIntelligenceOptions _diOptions;
    private readonly IChatClientProvider _chat;
    private readonly MockDocumentAnalysisService _fallback;
    private readonly ILogger<AzureDocumentIntelligenceAnalysisService> _logger;
    private readonly DocumentIntelligenceClient? _client;

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public AzureDocumentIntelligenceAnalysisService(
        IOptions<DocumentIntelligenceOptions> diOptions,
        IChatClientProvider chat,
        MockDocumentAnalysisService fallback,
        ILogger<AzureDocumentIntelligenceAnalysisService> logger)
    {
        _diOptions = diOptions.Value;
        _chat = chat;
        _fallback = fallback;
        _logger = logger;
        if (_diOptions.IsConfigured)
            _client = new DocumentIntelligenceClient(new Uri(_diOptions.Endpoint), new AzureKeyCredential(_diOptions.ApiKey));
    }

    public bool IsMock => false;

    public async Task<DocumentAnalysisResult> AnalyzeAsync(
        DocumentType declaredType, string fileName, byte[] content, Trade trade, CancellationToken ct = default)
    {
        // Demo JSON payloads (scenario runner) bypass OCR and use the deterministic mock.
        if (LooksLikeJson(content))
            return await _fallback.AnalyzeAsync(declaredType, fileName, content, trade, ct);

        if (_client is null || !_chat.IsAvailable)
            return await _fallback.AnalyzeAsync(declaredType, fileName, content, trade, ct);

        try
        {
            var ocrText = await RunOcrAsync(content, ct);
            if (string.IsNullOrWhiteSpace(ocrText))
                return await _fallback.AnalyzeAsync(declaredType, fileName, content, trade, ct);

            var extraction = await NormalizeAsync(declaredType, ocrText, ct);
            if (extraction is null)
                return await _fallback.AnalyzeAsync(declaredType, fileName, content, trade, ct);

            _logger.LogInformation("Document Intelligence OCR + LLM extraction succeeded for {File} ({Type}).",
                fileName, declaredType);
            return extraction;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Real OCR failed for {File}; falling back to mock analyzer.", fileName);
            return await _fallback.AnalyzeAsync(declaredType, fileName, content, trade, ct);
        }
    }

    private async Task<string> RunOcrAsync(byte[] content, CancellationToken ct)
    {
        var options = new AnalyzeDocumentOptions("prebuilt-layout", BinaryData.FromBytes(content));
        Operation<AnalyzeResult> op = await _client!.AnalyzeDocumentAsync(WaitUntil.Completed, options, ct);
        return op.Value.Content ?? string.Empty;
    }

    private async Task<DocumentAnalysisResult?> NormalizeAsync(DocumentType declaredType, string ocrText, CancellationToken ct)
    {
        var client = _chat.GetChatClient();
        if (client is null)
            return null;

        // Guard against very large documents blowing the context window.
        var text = ocrText.Length > 16000 ? ocrText[..16000] : ocrText;

        var system =
            "You are a trade-document data extraction engine. Extract structured fields from the OCR text of a " +
            "single international-trade document. Return ONLY a JSON object with these keys: " +
            "confidence (0..1 number), detectedType (one of: CommercialInvoice, PackingList, BillOfLading, AirWaybill, " +
            "AirTransferRelease, CertificateOfOrigin, InsuranceCertificate, InspectionCertificate), sourceLanguage (ISO code), riskLevel (Low|Medium|High), " +
            "totalAmount (number|null), currency (string|null), quantity (number|null), productDescription (string|null), " +
            "sellerName (string|null), buyerName (string|null), sellerWalletAddress (string|null), containerNumber (string|null), " +
            "shipmentDate (yyyy-MM-dd|null), incoterms (string|null), purchaseOrderNumber (string|null). " +
            "Report only what the document actually contains. Do not guess or invent values; use null when absent.";

        var user = $"Declared document type: {declaredType}\n\nOCR text:\n{text}";

        var options = new ChatCompletionOptions
        {
            ResponseFormat = ChatResponseFormat.CreateJsonObjectFormat()
        };

        ChatCompletion completion = await client.CompleteChatAsync(
            new ChatMessage[] { new SystemChatMessage(system), new UserChatMessage(user) }, options, ct);

        var json = completion.Content.Count > 0 ? completion.Content[0].Text : null;
        if (string.IsNullOrWhiteSpace(json))
            return null;

        var dto = JsonSerializer.Deserialize<OcrExtraction>(json, JsonOptions);
        if (dto is null)
            return null;

        var fields = new ExtractedDocumentFields
        {
            TotalAmount = dto.TotalAmount,
            Currency = Clean(dto.Currency),
            Quantity = dto.Quantity,
            ProductDescription = Clean(dto.ProductDescription),
            SellerName = Clean(dto.SellerName),
            BuyerName = Clean(dto.BuyerName),
            SellerWalletAddress = Clean(dto.SellerWalletAddress),
            ContainerNumber = Clean(dto.ContainerNumber),
            ShipmentDate = ParseDate(dto.ShipmentDate),
            Incoterms = Clean(dto.Incoterms),
            PurchaseOrderNumber = Clean(dto.PurchaseOrderNumber)
        };

        var confidence = dto.Confidence is >= 0 and <= 1 ? dto.Confidence!.Value : 0.9;
        var detected = Enum.TryParse<DocumentType>(dto.DetectedType, ignoreCase: true, out var dt) ? dt : (DocumentType?)null;
        var risk = dto.RiskLevel is "Low" or "Medium" or "High" ? dto.RiskLevel : RiskFromConfidence(confidence);

        return new DocumentAnalysisResult(fields, confidence, detected, Clean(dto.SourceLanguage) ?? "en", risk);
    }

    private static bool LooksLikeJson(byte[] content)
    {
        if (content.Length == 0)
            return false;
        var text = Encoding.UTF8.GetString(content, 0, Math.Min(content.Length, 64)).TrimStart();
        return text.Length > 0 && text[0] == '{';
    }

    private static string? Clean(string? s)
    {
        var t = s?.Trim();
        return string.IsNullOrEmpty(t) || string.Equals(t, "null", StringComparison.OrdinalIgnoreCase) ? null : t;
    }

    private static DateTime? ParseDate(string? s) =>
        DateTime.TryParse(s, out var d) ? d : null;

    private static string RiskFromConfidence(double confidence) =>
        confidence >= 0.85 ? "Low" : confidence >= 0.6 ? "Medium" : "High";

    private sealed class OcrExtraction
    {
        public double? Confidence { get; set; }
        public string? DetectedType { get; set; }
        public string? SourceLanguage { get; set; }
        public string? RiskLevel { get; set; }
        public decimal? TotalAmount { get; set; }
        public string? Currency { get; set; }
        public decimal? Quantity { get; set; }
        public string? ProductDescription { get; set; }
        public string? SellerName { get; set; }
        public string? BuyerName { get; set; }
        public string? SellerWalletAddress { get; set; }
        public string? ContainerNumber { get; set; }
        public string? ShipmentDate { get; set; }
        public string? Incoterms { get; set; }
        public string? PurchaseOrderNumber { get; set; }
    }
}

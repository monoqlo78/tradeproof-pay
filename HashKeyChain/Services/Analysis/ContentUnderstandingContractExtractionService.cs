using HashKeyChain.Domain;

namespace HashKeyChain.Services.Analysis;

/// <summary>
/// Contract extraction backed by Azure AI Content Understanding. Maps the custom
/// analyzer's fields onto <see cref="ExtractedTradeConditions"/>. Any failure
/// returns an empty (but available) result so the UI can report gracefully.
/// </summary>
public sealed class ContentUnderstandingContractExtractionService : IContractExtractionService
{
    private readonly ContentUnderstandingClient _client;
    private readonly ILogger<ContentUnderstandingContractExtractionService> _logger;

    public ContentUnderstandingContractExtractionService(
        ContentUnderstandingClient client,
        ILogger<ContentUnderstandingContractExtractionService> logger)
    {
        _client = client;
        _logger = logger;
    }

    public bool IsAvailable => _client.IsConfigured;

    public async Task<ContractExtractionResult> ExtractAsync(string fileName, byte[] content, CancellationToken ct = default)
    {
        if (!_client.IsConfigured)
            return ContractExtractionResult.Unavailable;

        var fields = await _client.AnalyzeAsync(content, ct);
        if (fields is null || fields.Count == 0)
            return ContractExtractionResult.Empty;

        var c = new ExtractedTradeConditions
        {
            DocumentType = Get(fields, "DocumentType"),
            BuyerName = Get(fields, "BuyerName"),
            SellerName = Get(fields, "SellerName"),
            BuyerWalletAddress = Get(fields, "BuyerWalletAddress"),
            SellerWalletAddress = Get(fields, "SellerWalletAddress"),
            PurchaseOrderNumber = Get(fields, "PurchaseOrderNumber"),
            ProductDescription = Get(fields, "ProductDescription"),
            Quantity = Num(fields, "Quantity"),
            QuantityUnit = Get(fields, "QuantityUnit"),
            GrossWeight = Get(fields, "GrossWeight"),
            TotalAmount = Num(fields, "TotalAmount"),
            Currency = Get(fields, "Currency"),
            Incoterms = Get(fields, "Incoterms"),
            ContainerNumber = Get(fields, "ContainerNumber"),
            LatestShipmentDate = Date(fields, "LatestShipmentDate"),
            PaymentExpiry = Date(fields, "PaymentExpiry"),
            OriginCountry = Get(fields, "OriginCountry"),
        };

        c.TransportMode = ResolveTransport(Get(fields, "TransportMode"), c.Incoterms, c.DocumentType);

        var hasData =
            c.BuyerName is not null || c.SellerName is not null || c.TotalAmount is not null ||
            c.Quantity is not null || c.ProductDescription is not null || c.PurchaseOrderNumber is not null;

        _logger.LogInformation("Content Understanding contract extraction for {File}: hasData={HasData}.", fileName, hasData);
        return new ContractExtractionResult(true, hasData, hasData ? c : null);
    }

    private static string? Get(IReadOnlyDictionary<string, CuFieldValue> f, string key) =>
        f.TryGetValue(key, out var v) ? v.Text : null;

    private static decimal? Num(IReadOnlyDictionary<string, CuFieldValue> f, string key) =>
        f.TryGetValue(key, out var v) ? v.AsNumber : null;

    private static DateTime? Date(IReadOnlyDictionary<string, CuFieldValue> f, string key) =>
        f.TryGetValue(key, out var v) ? v.AsDate : null;

    /// <summary>
    /// Resolve the transport mode from the extracted value, falling back to the
    /// Incoterms / document type (a Bill of Lading or CIF/FOB/CFR term implies Sea,
    /// an Air Waybill implies Air).
    /// </summary>
    private static TransportMode? ResolveTransport(string? raw, string? incoterms, string? docType)
    {
        var s = (raw ?? string.Empty).ToLowerInvariant();
        if (s.Contains("air")) return TransportMode.Air;
        if (s.Contains("sea") || s.Contains("ocean") || s.Contains("vessel")) return TransportMode.Sea;

        var dt = (docType ?? string.Empty).ToLowerInvariant();
        if (dt.Contains("airwaybill") || dt.Contains("air waybill")) return TransportMode.Air;
        if (dt.Contains("billoflading") || dt.Contains("bill of lading")) return TransportMode.Sea;

        var inc = (incoterms ?? string.Empty).ToUpperInvariant();
        if (inc.StartsWith("CIF") || inc.StartsWith("CFR") || inc.StartsWith("FOB") ||
            inc.StartsWith("FAS") || inc.Contains("SEA") || inc.Contains("PORT"))
            return TransportMode.Sea;

        return null;
    }
}

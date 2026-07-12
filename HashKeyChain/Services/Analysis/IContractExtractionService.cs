using HashKeyChain.Domain;

namespace HashKeyChain.Services.Analysis;

/// <summary>
/// Extracts trade conditions from an uploaded contract document so the trade
/// creation form can be pre-filled. Backed by Azure AI Content Understanding when
/// configured; otherwise reports unavailable so the UI hides the feature.
/// </summary>
public interface IContractExtractionService
{
    bool IsAvailable { get; }

    Task<ContractExtractionResult> ExtractAsync(string fileName, byte[] content, CancellationToken ct = default);
}

/// <summary>Trade conditions read off a contract (all optional).</summary>
public sealed class ExtractedTradeConditions
{
    public string? DocumentType { get; set; }
    public string? BuyerName { get; set; }
    public string? SellerName { get; set; }
    public string? BuyerWalletAddress { get; set; }
    public string? SellerWalletAddress { get; set; }
    public string? PurchaseOrderNumber { get; set; }
    public string? ProductDescription { get; set; }
    public decimal? Quantity { get; set; }
    public string? QuantityUnit { get; set; }
    public string? GrossWeight { get; set; }
    public decimal? TotalAmount { get; set; }
    public string? Currency { get; set; }
    public string? Incoterms { get; set; }
    public TransportMode? TransportMode { get; set; }
    public string? ContainerNumber { get; set; }
    public DateTime? LatestShipmentDate { get; set; }
    public DateTime? PaymentExpiry { get; set; }
    public string? OriginCountry { get; set; }
}

/// <summary>
/// Result of a contract extraction. <see cref="Available"/> is false when the
/// service is not configured; <see cref="HasData"/> is false when nothing usable
/// was extracted (e.g. an unreadable file).
/// </summary>
public sealed record ContractExtractionResult(
    bool Available,
    bool HasData,
    ExtractedTradeConditions? Conditions)
{
    public static ContractExtractionResult Unavailable => new(false, false, null);
    public static ContractExtractionResult Empty => new(true, false, null);
}

using HashKeyChain.Localization;

namespace HashKeyChain.Services.Analysis;

/// <summary>
/// Normalised fields extracted from a trade document by the analysis service
/// (spec §9). All fields are optional because different document types carry
/// different data; the rule engine only checks the fields relevant to each rule.
/// In DemoMode the mock analyzer can read these directly from a JSON payload so
/// scenarios can inject clean data or deliberate mismatches.
/// </summary>
public sealed class ExtractedDocumentFields
{
    public decimal? TotalAmount { get; set; }
    public string? Currency { get; set; }
    public decimal? Quantity { get; set; }
    public string? ProductDescription { get; set; }
    public string? SellerName { get; set; }
    public string? BuyerName { get; set; }

    /// <summary>Seller wallet as it appears on the document. Never used to change
    /// the authoritative trade seller wallet; a mismatch is a hard block (§5).</summary>
    public string? SellerWalletAddress { get; set; }

    public string? ContainerNumber { get; set; }
    public DateTime? ShipmentDate { get; set; }
    public string? Incoterms { get; set; }
    public string? PurchaseOrderNumber { get; set; }
}

/// <summary>Result of analyzing a single document version.</summary>
public sealed record DocumentAnalysisResult(
    ExtractedDocumentFields Fields,
    double Confidence,
    DocumentType? DetectedType,
    string? SourceLanguage,
    string? RiskLevel);

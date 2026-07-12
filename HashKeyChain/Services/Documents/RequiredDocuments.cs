using HashKeyChain.Domain;
using HashKeyChain.Localization;

namespace HashKeyChain.Services.Documents;

/// <summary>
/// Determines which document types are required for a trade based on its
/// transport mode (spec §2). Sea shipments require a Bill of Lading; air
/// shipments require an Air Waybill. Commercial Invoice and Packing List are
/// always required. The remaining types are optional extension documents.
/// </summary>
public static class RequiredDocuments
{
    public static IReadOnlyList<DocumentType> For(TransportMode mode)
    {
        var required = new List<DocumentType>
        {
            DocumentType.CommercialInvoice,
            DocumentType.PackingList
        };
        required.Add(mode == TransportMode.Air ? DocumentType.AirWaybill : DocumentType.BillOfLading);
        return required;
    }

    public static bool IsRequired(TransportMode mode, DocumentType type) => For(mode).Contains(type);
}

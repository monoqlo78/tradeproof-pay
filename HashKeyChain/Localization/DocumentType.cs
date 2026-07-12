namespace HashKeyChain.Localization;

/// <summary>
/// Trade document type. Internal values stay in English; the display label is
/// localized via <see cref="IEnumLocalizer"/>. The first four are MVP
/// auto-verified types (spec §2); the remainder are optional extension documents
/// that can be uploaded/stored/reviewed but are not part of the MVP automatic
/// judgement.
/// </summary>
public enum DocumentType
{
    // MVP auto-verified documents.
    CommercialInvoice,
    PackingList,
    BillOfLading,
    AirWaybill,

    // Optional extension documents (not auto-judged in MVP).
    AirTransferRelease,
    CertificateOfOrigin,
    InsuranceCertificate,
    InspectionCertificate
}

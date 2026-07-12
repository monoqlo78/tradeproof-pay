namespace HashKeyChain.Domain;

/// <summary>
/// Transport mode (spec §2). Determines the required transport document
/// (Sea → Bill of Lading, Air → Air Waybill).
/// </summary>
public enum TransportMode
{
    /// <summary>海上輸送。必須: Commercial Invoice / Packing List / Bill of Lading。</summary>
    Sea,

    /// <summary>航空輸送。必須: Commercial Invoice / Packing List / Air Waybill。</summary>
    Air
}

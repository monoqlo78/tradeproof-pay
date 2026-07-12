namespace HashKeyChain.Configuration;

/// <summary>
/// Organization-level demo settings that identify the operating ("own") company —
/// the buyer / importer that this instance is run by (spec: the app is operated by
/// the JP-side buyer). Persisted to a small JSON file, NOT the database, so no
/// schema change is required and existing trade data is never touched.
/// </summary>
public sealed class OrgSettings
{
    /// <summary>The company id that represents "self" (the buyer / importer).</summary>
    public int? OwnCompanyId { get; set; }

    /// <summary>Optional override wallet for the own company (funding source).</summary>
    public string? OwnWalletAddress { get; set; }
}

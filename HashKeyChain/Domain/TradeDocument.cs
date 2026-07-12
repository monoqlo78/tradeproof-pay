using System.ComponentModel.DataAnnotations;
using HashKeyChain.Localization;

namespace HashKeyChain.Domain;

/// <summary>
/// A logical document slot of a given <see cref="DocumentType"/> for a trade.
/// Holds an ordered set of <see cref="DocumentVersion"/>s; re-submitting the same
/// type keeps prior versions (superseded) and only the latest current version is
/// used for payment judgement (spec §8).
/// </summary>
public sealed class TradeDocument
{
    public int Id { get; set; }

    public int TradeId { get; set; }
    public Trade? Trade { get; set; }

    public DocumentType DocumentType { get; set; }

    /// <summary>Whether this document is required for the trade (vs optional extension doc).</summary>
    public bool IsRequired { get; set; }

    public ICollection<DocumentVersion> Versions { get; set; } = new List<DocumentVersion>();

    /// <summary>The current (latest, non-superseded) version, if any.</summary>
    public DocumentVersion? CurrentVersion =>
        Versions.Where(v => v.IsCurrent).OrderByDescending(v => v.Version).FirstOrDefault();
}

/// <summary>
/// A single uploaded version of a document (spec §8). Prior versions are never
/// deleted; they are marked superseded.
/// </summary>
public sealed class DocumentVersion
{
    public int Id { get; set; }

    public int TradeDocumentId { get; set; }
    public TradeDocument? TradeDocument { get; set; }

    public int Version { get; set; }

    [MaxLength(400)]
    public string FileName { get; set; } = string.Empty;

    /// <summary>Relative path in private storage. Original files are never stored on-chain (§20).</summary>
    [MaxLength(1024)]
    public string? StoragePath { get; set; }

    [MaxLength(128)]
    public string UploadedBy { get; set; } = string.Empty;

    public DateTime UploadedAtUtc { get; set; }

    /// <summary>SHA-256 of the original file (hex).</summary>
    [MaxLength(64)]
    public string Sha256 { get; set; } = string.Empty;

    public AnalysisStatus AnalysisStatus { get; set; } = AnalysisStatus.Pending;

    /// <summary>True for the latest valid version; false once superseded by a newer upload.</summary>
    public bool IsCurrent { get; set; } = true;

    [MaxLength(1024)]
    public string? RejectionReason { get; set; }

    /// <summary>Extracted fields as JSON (normalized dates/amounts/quantities/company names, container numbers, etc.).</summary>
    public string? ExtractedFieldsJson { get; set; }

    /// <summary>Overall analysis confidence 0..1.</summary>
    public double? Confidence { get; set; }

    [MaxLength(16)]
    public string? SourceLanguage { get; set; }

    /// <summary>Document type as detected by analysis (to cross-check the user-declared type, §9).</summary>
    public DocumentType? DetectedType { get; set; }
}

using System.ComponentModel.DataAnnotations;

namespace HashKeyChain.Domain;

/// <summary>
/// One execution of the deterministic rule engine over a trade's current
/// documents (spec §10/§11). Produces an overall <see cref="VerificationResult"/>
/// and a set of individual <see cref="DocumentCheck"/> findings.
/// </summary>
public sealed class VerificationRun
{
    public int Id { get; set; }

    public int TradeId { get; set; }
    public Trade? Trade { get; set; }

    public DateTime RunAtUtc { get; set; }

    public VerificationResult Result { get; set; }

    /// <summary>AI risk level label (Low/Medium/High) — informational only; never overrides a failed rule (§11).</summary>
    [MaxLength(16)]
    public string? AiRiskLevel { get; set; }

    [MaxLength(2048)]
    public string? Summary { get; set; }

    public ICollection<DocumentCheck> Checks { get; set; } = new List<DocumentCheck>();
}

/// <summary>
/// A single deterministic check result (e.g. "Invoice.TotalAmount == Trade.Amount",
/// "PackingList.ContainerNumber == BillOfLading.ContainerNumber").
/// </summary>
public sealed class DocumentCheck
{
    public int Id { get; set; }

    public int VerificationRunId { get; set; }
    public VerificationRun? VerificationRun { get; set; }

    /// <summary>Stable rule identifier, e.g. "Invoice_TotalAmount_MatchesTrade".</summary>
    [MaxLength(128)]
    public string RuleKey { get; set; } = string.Empty;

    public bool Passed { get; set; }

    /// <summary>True when a failure only warrants manual review rather than a hard block.</summary>
    public bool IsSoft { get; set; }

    [MaxLength(512)]
    public string? ExpectedValue { get; set; }

    [MaxLength(512)]
    public string? ActualValue { get; set; }

    [MaxLength(1024)]
    public string? Detail { get; set; }
}

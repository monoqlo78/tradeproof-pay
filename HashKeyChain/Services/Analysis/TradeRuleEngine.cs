using HashKeyChain.Domain;
using HashKeyChain.Localization;

namespace HashKeyChain.Services.Analysis;

/// <summary>A document that has been analyzed, ready for rule evaluation.</summary>
public sealed record AnalyzedDocument(
    DocumentType DeclaredType,
    DocumentType? DetectedType,
    double Confidence,
    ExtractedDocumentFields Fields);

/// <summary>Everything the rule engine needs to judge a trade.</summary>
public sealed record RuleContext(
    Trade Trade,
    string? BuyerCompanyName,
    string? SellerCompanyName,
    IReadOnlyList<AnalyzedDocument> Documents,
    double ConfidenceThreshold = 0.75);

/// <summary>
/// Deterministic verification rule engine (spec §10/§11). It cross-checks the
/// analyzed documents against each other and against the locked trade conditions.
/// A hard-rule failure forces <see cref="VerificationResult.Blocked"/>; a soft
/// failure (with no hard failure) forces <see cref="VerificationResult.ManualReview"/>;
/// all-clear yields <see cref="VerificationResult.Pass"/>. An AI risk label never
/// overrides a failed deterministic rule.
/// </summary>
public interface ITradeRuleEngine
{
    VerificationRun Evaluate(RuleContext context);
}

public sealed class TradeRuleEngine : ITradeRuleEngine
{
    public VerificationRun Evaluate(RuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var run = new VerificationRun { TradeId = context.Trade.Id, RunAtUtc = DateTime.UtcNow };
        var checks = run.Checks;

        var byType = context.Documents
            .GroupBy(d => d.DeclaredType)
            .ToDictionary(g => g.Key, g => g.Last());

        // --- Required documents present (hard) ---
        foreach (var required in RequiredTypes(context.Trade.TransportMode))
        {
            var present = byType.ContainsKey(required);
            checks.Add(Check($"Required_{required}", present, soft: false,
                expected: "present", actual: present ? "present" : "missing",
                detail: present ? null : $"Required document {required} is missing."));
        }

        var invoice = byType.GetValueOrDefault(DocumentType.CommercialInvoice);
        var packing = byType.GetValueOrDefault(DocumentType.PackingList);
        var transport = byType.GetValueOrDefault(DocumentType.BillOfLading)
                        ?? byType.GetValueOrDefault(DocumentType.AirWaybill);

        // --- Invoice total == trade amount (hard) ---
        if (invoice is not null)
        {
            var actual = invoice.Fields.TotalAmount;
            var ok = actual.HasValue && actual.Value == context.Trade.PaymentAmount;
            checks.Add(Check("Invoice_TotalAmount_MatchesTrade", ok, soft: false,
                expected: context.Trade.PaymentAmount.ToString(),
                actual: actual?.ToString() ?? "(none)"));

            // --- Currency/token match (hard) ---
            var curOk = !string.IsNullOrWhiteSpace(invoice.Fields.Currency)
                        && string.Equals(invoice.Fields.Currency, context.Trade.Currency, StringComparison.OrdinalIgnoreCase);
            checks.Add(Check("Invoice_Currency_MatchesTrade", curOk, soft: false,
                expected: context.Trade.Currency, actual: invoice.Fields.Currency ?? "(none)"));

            // --- Seller wallet not altered by invoice (hard, §5) ---
            var docWallet = invoice.Fields.SellerWalletAddress;
            var walletOk = string.IsNullOrWhiteSpace(docWallet)
                           || string.Equals(docWallet, context.Trade.SellerWalletAddress, StringComparison.OrdinalIgnoreCase);
            checks.Add(Check("SellerWallet_NotAltered", walletOk, soft: false,
                expected: context.Trade.SellerWalletAddress, actual: docWallet ?? "(not present)",
                detail: walletOk ? null : "Document seller wallet differs from the authoritative trade wallet."));

            // --- Seller name match (soft) ---
            if (!string.IsNullOrWhiteSpace(context.SellerCompanyName) && !string.IsNullOrWhiteSpace(invoice.Fields.SellerName))
            {
                var nameOk = NameMatches(invoice.Fields.SellerName!, context.SellerCompanyName!);
                checks.Add(Check("Invoice_SellerName_MatchesTrade", nameOk, soft: true,
                    expected: context.SellerCompanyName, actual: invoice.Fields.SellerName));
            }
        }

        // --- Quantity match (hard, only when trade specifies expected quantity) ---
        if (context.Trade.ExpectedQuantity is { } expectedQty && packing is not null)
        {
            var actualQty = packing.Fields.Quantity;
            var qtyOk = actualQty.HasValue && actualQty.Value == expectedQty;
            checks.Add(Check("PackingList_Quantity_MatchesTrade", qtyOk, soft: false,
                expected: expectedQty.ToString(), actual: actualQty?.ToString() ?? "(none)"));
        }

        // --- Container number consistency between packing list and transport doc (hard) ---
        if (packing is not null && transport is not null
            && !string.IsNullOrWhiteSpace(packing.Fields.ContainerNumber)
            && !string.IsNullOrWhiteSpace(transport.Fields.ContainerNumber))
        {
            var contOk = string.Equals(packing.Fields.ContainerNumber, transport.Fields.ContainerNumber, StringComparison.OrdinalIgnoreCase);
            checks.Add(Check("Container_Consistency", contOk, soft: false,
                expected: packing.Fields.ContainerNumber, actual: transport.Fields.ContainerNumber));
        }

        // --- Shipment date within latest shipment date (hard) ---
        if (context.Trade.LatestShipmentDate is { } latest && transport?.Fields.ShipmentDate is { } shipped)
        {
            var dateOk = shipped.Date <= latest.Date;
            checks.Add(Check("ShipmentDate_WithinLatest", dateOk, soft: false,
                expected: $"<= {latest:yyyy-MM-dd}", actual: shipped.ToString("yyyy-MM-dd")));
        }

        // --- Confidence above threshold (soft) ---
        foreach (var doc in context.Documents)
        {
            var confOk = doc.Confidence >= context.ConfidenceThreshold;
            checks.Add(Check($"Confidence_{doc.DeclaredType}", confOk, soft: true,
                expected: $">= {context.ConfidenceThreshold:0.00}", actual: doc.Confidence.ToString("0.00")));

            // --- Declared type matches detected type (soft, §9) ---
            if (doc.DetectedType is { } detected)
            {
                var typeOk = detected == doc.DeclaredType;
                checks.Add(Check($"DeclaredType_Matches_{doc.DeclaredType}", typeOk, soft: true,
                    expected: doc.DeclaredType.ToString(), actual: detected.ToString()));
            }
        }

        var hardFail = checks.Any(c => !c.Passed && !c.IsSoft);
        var softFail = checks.Any(c => !c.Passed && c.IsSoft);

        run.Result = hardFail ? VerificationResult.Blocked
            : softFail ? VerificationResult.ManualReview
            : VerificationResult.Pass;

        run.AiRiskLevel = context.Documents.Count == 0 ? null
            : context.Documents.Min(d => d.Confidence) is var minConf && minConf >= 0.85 ? "Low"
            : minConf >= 0.6 ? "Medium" : "High";

        run.Summary = BuildSummary(run, hardFail, softFail);
        return run;
    }

    private static IReadOnlyList<DocumentType> RequiredTypes(TransportMode mode) => new List<DocumentType>
    {
        DocumentType.CommercialInvoice,
        DocumentType.PackingList,
        mode == TransportMode.Air ? DocumentType.AirWaybill : DocumentType.BillOfLading
    };

    private static bool NameMatches(string a, string b)
    {
        static string Norm(string s) => new string(s.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        var na = Norm(a);
        var nb = Norm(b);
        return na.Length > 0 && nb.Length > 0 && (na.Contains(nb) || nb.Contains(na));
    }

    private static DocumentCheck Check(string ruleKey, bool passed, bool soft,
        string? expected = null, string? actual = null, string? detail = null) => new()
    {
        RuleKey = ruleKey,
        Passed = passed,
        IsSoft = soft,
        ExpectedValue = Trunc(expected, 512),
        ActualValue = Trunc(actual, 512),
        Detail = Trunc(detail, 1024)
    };

    private static string? Trunc(string? s, int max) =>
        s is null ? null : s.Length <= max ? s : s[..max];

    private static string BuildSummary(VerificationRun run, bool hardFail, bool softFail)
    {
        var failed = run.Checks.Where(c => !c.Passed).Select(c => c.RuleKey).ToList();
        return run.Result switch
        {
            VerificationResult.Pass => "All deterministic checks passed.",
            VerificationResult.ManualReview => $"Manual review required (soft failures: {string.Join(", ", failed)}).",
            VerificationResult.Blocked => $"Blocked by hard-rule failures: {string.Join(", ", failed)}.",
            _ => string.Empty
        };
    }
}

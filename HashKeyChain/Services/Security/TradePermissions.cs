using HashKeyChain.Domain;

namespace HashKeyChain.Services.Security;

/// <summary>
/// Business operations that are subject to role-based authorization (spec §4).
/// </summary>
public enum TradeOperation
{
    CreateTrade,
    EditConditions,
    RequestApproval,
    ApproveConditions,
    FundEscrow,
    SubmitDocument,
    RunAnalysis,
    VerifyDocuments,
    RejectDocuments,
    RaiseManualReview,
    ApprovePayment,
    SignTransaction,
    Refund,
    CancelTrade,
    ManageDemoData
}

/// <summary>
/// Static role → operation policy derived from spec §4. Deliberately explicit so
/// the separation of duties (e.g. a Trade Verifier can never sign a payment, a
/// Buyer Operator can never give final approval) is auditable in one place.
/// </summary>
public static class TradePermissions
{
    private static readonly IReadOnlyDictionary<TradeOperation, TradeRole[]> Policy =
        new Dictionary<TradeOperation, TradeRole[]>
        {
            [TradeOperation.CreateTrade] = new[] { TradeRole.BuyerOperator, TradeRole.Administrator },
            [TradeOperation.EditConditions] = new[] { TradeRole.BuyerOperator, TradeRole.Administrator },
            [TradeOperation.RequestApproval] = new[] { TradeRole.BuyerOperator, TradeRole.Administrator },
            [TradeOperation.ApproveConditions] = new[] { TradeRole.BuyerApprover, TradeRole.Administrator },
            [TradeOperation.FundEscrow] = new[] { TradeRole.BuyerApprover, TradeRole.Administrator },
            [TradeOperation.SubmitDocument] = new[] { TradeRole.Seller, TradeRole.BuyerOperator, TradeRole.Administrator },
            [TradeOperation.RunAnalysis] = new[] { TradeRole.Seller, TradeRole.TradeVerifier, TradeRole.BuyerOperator, TradeRole.Administrator },
            [TradeOperation.VerifyDocuments] = new[] { TradeRole.TradeVerifier, TradeRole.Administrator },
            [TradeOperation.RejectDocuments] = new[] { TradeRole.TradeVerifier, TradeRole.BuyerOperator, TradeRole.Administrator },
            [TradeOperation.RaiseManualReview] = new[] { TradeRole.TradeVerifier, TradeRole.Administrator },
            [TradeOperation.ApprovePayment] = new[] { TradeRole.BuyerApprover, TradeRole.Administrator },
            [TradeOperation.SignTransaction] = new[] { TradeRole.BuyerApprover, TradeRole.Administrator },
            [TradeOperation.Refund] = new[] { TradeRole.BuyerApprover, TradeRole.Administrator },
            [TradeOperation.CancelTrade] = new[] { TradeRole.BuyerOperator, TradeRole.BuyerApprover, TradeRole.Administrator },
            [TradeOperation.ManageDemoData] = new[] { TradeRole.BuyerOperator, TradeRole.Administrator }
        };

    public static IReadOnlyCollection<TradeRole> RolesFor(TradeOperation operation) =>
        Policy.TryGetValue(operation, out var roles) ? roles : Array.Empty<TradeRole>();

    public static bool IsAllowed(TradeOperation operation, IEnumerable<TradeRole> heldRoles)
    {
        var allowed = RolesFor(operation);
        return heldRoles.Any(allowed.Contains);
    }

    public static bool IsAllowed(TradeOperation operation, TradeRole role) =>
        RolesFor(operation).Contains(role);
}

using HashKeyChain.Localization;

namespace HashKeyChain.Services.Trades;

/// <summary>Thrown when an illegal <see cref="TradeStatus"/> transition is attempted.</summary>
public sealed class InvalidTradeTransitionException(TradeStatus from, TradeStatus to)
    : InvalidOperationException($"Illegal trade transition {from} → {to}.")
{
    public TradeStatus From { get; } = from;
    public TradeStatus To { get; } = to;
}

/// <summary>
/// Owns the trade lifecycle (spec §23). All status changes must go through this
/// service; UI/controllers never mutate <see cref="Domain.Trade.Status"/>
/// directly. The transition table encodes the standard business flow and the
/// exceptional paths (rejection, block, expiry, refund, cancellation).
/// </summary>
public interface ITradeStateMachine
{
    IReadOnlySet<TradeStatus> AllowedNext(TradeStatus from);
    bool CanTransition(TradeStatus from, TradeStatus to);

    /// <summary>Validates and applies the transition on the trade, updating the
    /// timestamp. Throws <see cref="InvalidTradeTransitionException"/> if illegal.</summary>
    void Transition(Domain.Trade trade, TradeStatus to);
}

public sealed class TradeStateMachine : ITradeStateMachine
{
    // States from which an escrow-funded trade may expire (spec §18).
    private static readonly TradeStatus[] ExpirableFundedStates =
    {
        TradeStatus.Funded, TradeStatus.AwaitingDocuments, TradeStatus.Analyzing,
        TradeStatus.ManualReview, TradeStatus.DocumentsRejected, TradeStatus.Blocked,
        TradeStatus.ReadyForVerification, TradeStatus.ReadyForApproval, TradeStatus.Approved
    };

    private static readonly IReadOnlyDictionary<TradeStatus, HashSet<TradeStatus>> Transitions = Build();

    public IReadOnlySet<TradeStatus> AllowedNext(TradeStatus from) =>
        Transitions.TryGetValue(from, out var set) ? set : new HashSet<TradeStatus>();

    public bool CanTransition(TradeStatus from, TradeStatus to) =>
        from == to || (Transitions.TryGetValue(from, out var set) && set.Contains(to));

    public void Transition(Domain.Trade trade, TradeStatus to)
    {
        ArgumentNullException.ThrowIfNull(trade);
        if (trade.Status == to)
            return;
        if (!CanTransition(trade.Status, to))
            throw new InvalidTradeTransitionException(trade.Status, to);
        trade.Status = to;
        trade.UpdatedAtUtc = DateTime.UtcNow;
    }

    private static IReadOnlyDictionary<TradeStatus, HashSet<TradeStatus>> Build()
    {
        var t = new Dictionary<TradeStatus, HashSet<TradeStatus>>
        {
            [TradeStatus.Draft] = new() { TradeStatus.PendingTradeApproval, TradeStatus.Cancelled },
            [TradeStatus.PendingTradeApproval] = new() { TradeStatus.AwaitingFunding, TradeStatus.Draft, TradeStatus.Cancelled },
            [TradeStatus.AwaitingFunding] = new() { TradeStatus.Funded, TradeStatus.Cancelled, TradeStatus.Expired },
            [TradeStatus.Funded] = new() { TradeStatus.AwaitingDocuments },
            [TradeStatus.AwaitingDocuments] = new() { TradeStatus.Analyzing },
            [TradeStatus.Analyzing] = new()
            {
                TradeStatus.ReadyForVerification, TradeStatus.ManualReview,
                TradeStatus.Blocked, TradeStatus.DocumentsRejected, TradeStatus.AwaitingDocuments
            },
            [TradeStatus.ManualReview] = new()
            {
                TradeStatus.ReadyForVerification, TradeStatus.DocumentsRejected, TradeStatus.Blocked
            },
            [TradeStatus.Blocked] = new()
            {
                TradeStatus.ManualReview, TradeStatus.DocumentsRejected, TradeStatus.Analyzing
            },
            [TradeStatus.DocumentsRejected] = new() { TradeStatus.AwaitingDocuments },
            [TradeStatus.ReadyForVerification] = new()
            {
                TradeStatus.ReadyForApproval, TradeStatus.ManualReview,
                TradeStatus.DocumentsRejected, TradeStatus.Blocked
            },
            [TradeStatus.ReadyForApproval] = new()
            {
                TradeStatus.Approved, TradeStatus.DocumentsRejected, TradeStatus.ManualReview
            },
            [TradeStatus.Approved] = new() { TradeStatus.SettlementPending, TradeStatus.ReadyForApproval },
            [TradeStatus.SettlementPending] = new() { TradeStatus.Settled, TradeStatus.ReadyForApproval },
            [TradeStatus.Expired] = new() { TradeStatus.RefundPending },
            [TradeStatus.RefundPending] = new() { TradeStatus.Refunded, TradeStatus.Expired },
            // Terminal states.
            [TradeStatus.Settled] = new(),
            [TradeStatus.Refunded] = new(),
            [TradeStatus.Cancelled] = new()
        };

        // Any escrow-funded, pre-settlement state may expire (spec §18).
        foreach (var s in ExpirableFundedStates)
            t[s].Add(TradeStatus.Expired);

        return t;
    }
}

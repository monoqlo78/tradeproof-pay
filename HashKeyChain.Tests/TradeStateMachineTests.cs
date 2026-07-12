using HashKeyChain.Domain;
using HashKeyChain.Localization;
using HashKeyChain.Services.Trades;

namespace HashKeyChain.Tests;

/// <summary>
/// Unit tests for the trade state machine (spec §23). They lock in the legal
/// happy path, the exceptional branches (rejection, block, expiry, refund) and
/// that illegal jumps are rejected.
/// </summary>
public class TradeStateMachineTests
{
    private readonly ITradeStateMachine _sm = new TradeStateMachine();

    private static Trade NewTrade(TradeStatus status) =>
        new() { Status = status, TradeReference = "T-1" };

    [Theory]
    [InlineData(TradeStatus.Draft, TradeStatus.PendingTradeApproval)]
    [InlineData(TradeStatus.PendingTradeApproval, TradeStatus.AwaitingFunding)]
    [InlineData(TradeStatus.AwaitingFunding, TradeStatus.Funded)]
    [InlineData(TradeStatus.Funded, TradeStatus.AwaitingDocuments)]
    [InlineData(TradeStatus.AwaitingDocuments, TradeStatus.Analyzing)]
    [InlineData(TradeStatus.Analyzing, TradeStatus.ReadyForVerification)]
    [InlineData(TradeStatus.ReadyForVerification, TradeStatus.ReadyForApproval)]
    [InlineData(TradeStatus.ReadyForApproval, TradeStatus.Approved)]
    [InlineData(TradeStatus.Approved, TradeStatus.SettlementPending)]
    [InlineData(TradeStatus.SettlementPending, TradeStatus.Settled)]
    public void Allows_happy_path(TradeStatus from, TradeStatus to)
    {
        Assert.True(_sm.CanTransition(from, to));
    }

    [Theory]
    [InlineData(TradeStatus.Draft, TradeStatus.Settled)]
    [InlineData(TradeStatus.AwaitingFunding, TradeStatus.Approved)]
    [InlineData(TradeStatus.Settled, TradeStatus.Draft)]
    [InlineData(TradeStatus.Refunded, TradeStatus.Funded)]
    [InlineData(TradeStatus.Cancelled, TradeStatus.Draft)]
    public void Rejects_illegal_jumps(TradeStatus from, TradeStatus to)
    {
        Assert.False(_sm.CanTransition(from, to));
        var trade = NewTrade(from);
        Assert.Throws<InvalidTradeTransitionException>(() => _sm.Transition(trade, to));
        Assert.Equal(from, trade.Status);
    }

    [Fact]
    public void Transition_updates_status_and_timestamp()
    {
        var trade = NewTrade(TradeStatus.Draft);
        var before = trade.UpdatedAtUtc;
        _sm.Transition(trade, TradeStatus.PendingTradeApproval);
        Assert.Equal(TradeStatus.PendingTradeApproval, trade.Status);
        Assert.True(trade.UpdatedAtUtc >= before);
    }

    [Fact]
    public void Same_state_is_noop()
    {
        var trade = NewTrade(TradeStatus.Funded);
        _sm.Transition(trade, TradeStatus.Funded);
        Assert.Equal(TradeStatus.Funded, trade.Status);
    }

    [Theory]
    [InlineData(TradeStatus.Funded)]
    [InlineData(TradeStatus.AwaitingDocuments)]
    [InlineData(TradeStatus.ReadyForApproval)]
    [InlineData(TradeStatus.Approved)]
    public void Funded_states_can_expire(TradeStatus from)
    {
        Assert.True(_sm.CanTransition(from, TradeStatus.Expired));
    }

    [Fact]
    public void Expiry_leads_to_refund()
    {
        Assert.True(_sm.CanTransition(TradeStatus.Expired, TradeStatus.RefundPending));
        Assert.True(_sm.CanTransition(TradeStatus.RefundPending, TradeStatus.Refunded));
    }

    [Fact]
    public void Terminal_states_have_no_exits()
    {
        Assert.Empty(_sm.AllowedNext(TradeStatus.Settled));
        Assert.Empty(_sm.AllowedNext(TradeStatus.Refunded));
        Assert.Empty(_sm.AllowedNext(TradeStatus.Cancelled));
    }
}

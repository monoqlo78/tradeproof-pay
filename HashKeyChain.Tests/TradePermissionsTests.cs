using HashKeyChain.Domain;
using HashKeyChain.Services.Security;

namespace HashKeyChain.Tests;

/// <summary>
/// Unit tests for the role → operation policy (spec §4). They lock in the
/// separation of duties: an operator cannot approve, a verifier cannot sign.
/// </summary>
public class TradePermissionsTests
{
    [Theory]
    [InlineData(TradeOperation.CreateTrade, TradeRole.BuyerOperator, true)]
    [InlineData(TradeOperation.ApproveConditions, TradeRole.BuyerApprover, true)]
    [InlineData(TradeOperation.VerifyDocuments, TradeRole.TradeVerifier, true)]
    [InlineData(TradeOperation.SubmitDocument, TradeRole.Seller, true)]
    [InlineData(TradeOperation.ApprovePayment, TradeRole.BuyerApprover, true)]
    public void Grants_expected_roles(TradeOperation op, TradeRole role, bool expected)
    {
        Assert.Equal(expected, TradePermissions.IsAllowed(op, role));
    }

    [Theory]
    [InlineData(TradeOperation.ApprovePayment, TradeRole.BuyerOperator)]
    [InlineData(TradeOperation.SignTransaction, TradeRole.TradeVerifier)]
    [InlineData(TradeOperation.VerifyDocuments, TradeRole.Seller)]
    [InlineData(TradeOperation.CreateTrade, TradeRole.Seller)]
    public void Denies_separation_of_duties_violations(TradeOperation op, TradeRole role)
    {
        Assert.False(TradePermissions.IsAllowed(op, role));
    }

    [Fact]
    public void CurrentUserContext_uses_active_role()
    {
        var user = new AppUser { DisplayName = "Op", Roles = { new UserRole { Role = TradeRole.BuyerOperator } } };
        var ctx = new CurrentUserContext();
        ctx.SetUser(user);

        Assert.True(ctx.IsAuthenticated);
        Assert.Equal(TradeRole.BuyerOperator, ctx.ActiveRole);
        Assert.True(ctx.Can(TradeOperation.CreateTrade));
        Assert.False(ctx.Can(TradeOperation.ApprovePayment));
    }

    [Fact]
    public void CurrentUserContext_rejects_unheld_role()
    {
        var user = new AppUser { DisplayName = "Op", Roles = { new UserRole { Role = TradeRole.BuyerOperator } } };
        var ctx = new CurrentUserContext();
        ctx.SetUser(user);

        Assert.Throws<InvalidOperationException>(() => ctx.SetActiveRole(TradeRole.BuyerApprover));
    }
}

using Microsoft.Extensions.Localization;

namespace HashKeyChain.Localization;

/// <summary>
/// User-facing Web3 / wallet status messages. The identifiers are English; the
/// smart-contract custom error names are also kept in English and mapped here to
/// a localized user message.
/// </summary>
public enum Web3Message
{
    ConnectWallet,
    SwitchNetwork,
    ApprovingAllowance,
    FundingEscrow,
    WaitingConfirmation,
    SettlementCompleted,
    AlreadySettled,
    WalletRejected,
    TransactionFailed
}

public interface IWeb3MessageLocalizer
{
    string Localize(Web3Message message);

    /// <summary>
    /// Translates a smart-contract custom error name (kept in English) into a
    /// localized, user-friendly message.
    /// </summary>
    string LocalizeCustomError(string customErrorName);
}

/// <inheritdoc />
public sealed class Web3MessageLocalizer : IWeb3MessageLocalizer
{
    private static readonly IReadOnlyDictionary<string, Web3Message> CustomErrorMap =
        new Dictionary<string, Web3Message>(StringComparer.OrdinalIgnoreCase)
        {
            ["AlreadySettled"] = Web3Message.AlreadySettled,
            ["TradeAlreadySettled"] = Web3Message.AlreadySettled,
            ["DuplicateSettlement"] = Web3Message.AlreadySettled,
            ["InsufficientAllowance"] = Web3Message.ApprovingAllowance,
            ["WrongNetwork"] = Web3Message.SwitchNetwork,
            ["WalletRejected"] = Web3Message.WalletRejected,
            ["UserRejected"] = Web3Message.WalletRejected,
        };

    private readonly IStringLocalizer<SharedResource> _localizer;

    public Web3MessageLocalizer(IStringLocalizer<SharedResource> localizer) => _localizer = localizer;

    public string Localize(Web3Message message) => _localizer[$"Web3_{message}"];

    public string LocalizeCustomError(string customErrorName)
    {
        var message = CustomErrorMap.TryGetValue(customErrorName, out var mapped)
            ? mapped
            : Web3Message.TransactionFailed;

        return Localize(message);
    }
}

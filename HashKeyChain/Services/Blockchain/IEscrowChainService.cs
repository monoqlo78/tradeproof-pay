namespace HashKeyChain.Services.Blockchain;

/// <summary>Result of an on-chain (or mock) operation, capturing the fields
/// required for display and audit (chain doc §4).</summary>
public sealed record ChainTxResult(
    bool Success,
    string? TransactionHash,
    long? BlockNumber,
    string? FromAddress,
    string? ToAddress,
    string? ContractAddress,
    int ChainId,
    long? GasUsed,
    string Status,
    string? ExplorerUrl,
    string? Error = null);

/// <summary>Snapshot of the escrow state for a trade as seen on-chain.</summary>
public sealed record EscrowState(
    bool Exists,
    bool IsFunded,
    bool IsReleased,
    bool IsRefunded,
    decimal FundedAmount,
    string? TokenAddress);

/// <summary>A pre-flight estimate for a wallet operation (chain doc §11). The
/// values are estimates only and must be shown as such in the UI.</summary>
public sealed record GasEstimate(long GasLimit, string? MaxFeePerGas, decimal? EstimatedTotal, string CurrencySymbol);

/// <summary>
/// Abstraction over the escrow contract. DemoMode uses an in-process mock;
/// Testnet/Mainnet implementations talk to HashKey Chain. A settlement/refund is
/// only ever treated as final once the receipt status is confirmed successful on
/// the server (chain doc §4/§17). Private keys are never held server-side; buyer
/// wallet signing happens client-side — server implementations submit already
/// signed transactions or observe on-chain state.
/// </summary>
public interface IEscrowChainService
{
    bool IsMock { get; }

    Task<EscrowState> GetEscrowStateAsync(int tradeId, CancellationToken ct = default);

    Task<GasEstimate> EstimateAsync(string operation, CancellationToken ct = default);

    /// <summary>Fund the escrow for a trade (spec §7).</summary>
    Task<ChainTxResult> FundAsync(int tradeId, string buyerWallet, decimal amount, string token, CancellationToken ct = default);

    /// <summary>Register document / verdict / approval hashes on-chain (spec §20).</summary>
    Task<ChainTxResult> RegisterHashesAsync(int tradeId, IReadOnlyDictionary<string, string> hashes, CancellationToken ct = default);

    /// <summary>Release funds to the seller (spec §16). Rejects double settlement.</summary>
    Task<ChainTxResult> ReleaseAsync(int tradeId, string sellerWallet, decimal amount, CancellationToken ct = default);

    /// <summary>Refund the buyer after expiry (spec §18). Rejects double refund.</summary>
    Task<ChainTxResult> RefundAsync(int tradeId, string buyerWallet, CancellationToken ct = default);
}

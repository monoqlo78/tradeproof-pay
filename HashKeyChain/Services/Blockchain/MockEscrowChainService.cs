using System.Collections.Concurrent;
using System.Security.Cryptography;
using HashKeyChain.Configuration;
using Microsoft.Extensions.Options;

namespace HashKeyChain.Services.Blockchain;

/// <summary>
/// In-process mock escrow used in DemoMode. Simulates the escrow contract's
/// invariants — funding, single release, single refund, double-settlement
/// rejection — so the full business flow works locally without a live chain.
/// Produces deterministic pseudo transaction hashes and (when an Explorer base
/// URL is configured) Explorer-style links.
/// </summary>
public sealed class MockEscrowChainService : IEscrowChainService
{
    private sealed class MockEscrow
    {
        public bool Funded;
        public bool Released;
        public bool Refunded;
        public decimal Amount;
        public string? Token;
    }

    private static readonly ConcurrentDictionary<int, MockEscrow> Escrows = new();

    private readonly BlockchainOptions _options;

    public MockEscrowChainService(IOptions<BlockchainOptions> options) => _options = options.Value;

    public bool IsMock => true;

    public Task<EscrowState> GetEscrowStateAsync(int tradeId, CancellationToken ct = default)
    {
        if (Escrows.TryGetValue(tradeId, out var e))
            return Task.FromResult(new EscrowState(true, e.Funded, e.Released, e.Refunded, e.Amount, e.Token));
        return Task.FromResult(new EscrowState(false, false, false, false, 0m, null));
    }

    public Task<GasEstimate> EstimateAsync(string operation, CancellationToken ct = default) =>
        Task.FromResult(new GasEstimate(90_000, "0.0", 0m, _options.NativeCurrencySymbol));

    public Task<ChainTxResult> FundAsync(int tradeId, string buyerWallet, decimal amount, string token, CancellationToken ct = default)
    {
        var e = Escrows.GetOrAdd(tradeId, _ => new MockEscrow());
        lock (e)
        {
            if (e.Funded)
                return Task.FromResult(Fail(tradeId, "Fund", "Escrow already funded."));
            e.Funded = true;
            e.Amount = amount;
            e.Token = token;
        }
        return Task.FromResult(Ok(tradeId, "Fund", buyerWallet, _options.EscrowContractAddress));
    }

    public Task<ChainTxResult> RegisterHashesAsync(int tradeId, IReadOnlyDictionary<string, string> hashes, CancellationToken ct = default) =>
        Task.FromResult(Ok(tradeId, "RegisterHashes", null, _options.EscrowContractAddress));

    public Task<ChainTxResult> ReleaseAsync(int tradeId, string sellerWallet, decimal amount, CancellationToken ct = default)
    {
        if (!Escrows.TryGetValue(tradeId, out var e) || !e.Funded)
            return Task.FromResult(Fail(tradeId, "Release", "Escrow is not funded."));
        lock (e)
        {
            if (e.Released)
                return Task.FromResult(Fail(tradeId, "Release", "Trade already settled (double settlement rejected)."));
            if (e.Refunded)
                return Task.FromResult(Fail(tradeId, "Release", "Trade already refunded."));
            e.Released = true;
        }
        return Task.FromResult(Ok(tradeId, "Release", _options.EscrowContractAddress, sellerWallet));
    }

    public Task<ChainTxResult> RefundAsync(int tradeId, string buyerWallet, CancellationToken ct = default)
    {
        if (!Escrows.TryGetValue(tradeId, out var e) || !e.Funded)
            return Task.FromResult(Fail(tradeId, "Refund", "Escrow is not funded."));
        lock (e)
        {
            if (e.Released)
                return Task.FromResult(Fail(tradeId, "Refund", "Trade already settled; cannot refund."));
            if (e.Refunded)
                return Task.FromResult(Fail(tradeId, "Refund", "Trade already refunded (double refund rejected)."));
            e.Refunded = true;
        }
        return Task.FromResult(Ok(tradeId, "Refund", _options.EscrowContractAddress, buyerWallet));
    }

    private ChainTxResult Ok(int tradeId, string op, string? from, string? to)
    {
        var txHash = PseudoTxHash(tradeId, op);
        return new ChainTxResult(
            Success: true,
            TransactionHash: txHash,
            BlockNumber: Random.Shared.Next(1_000_000, 9_000_000),
            FromAddress: from,
            ToAddress: to,
            ContractAddress: _options.EscrowContractAddress,
            ChainId: _options.ChainId,
            GasUsed: Random.Shared.Next(40_000, 120_000),
            Status: "Success",
            ExplorerUrl: _options.TransactionUrl(txHash));
    }

    private ChainTxResult Fail(int tradeId, string op, string error) =>
        new(false, null, null, null, null, _options.EscrowContractAddress, _options.ChainId, null, "Failed", null, error);

    private static string PseudoTxHash(int tradeId, string op)
    {
        var bytes = SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes($"{tradeId}:{op}:{Guid.NewGuid():N}"));
        return "0x" + Convert.ToHexString(bytes).ToLowerInvariant();
    }
}

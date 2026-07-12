using System.Numerics;
using HashKeyChain.Configuration;
using Microsoft.Extensions.Options;
using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;
using Nethereum.Hex.HexConvertors.Extensions;
using Nethereum.Util;
using Nethereum.Web3;
using Nethereum.Web3.Accounts;

namespace HashKeyChain.Services.Blockchain;

/// <summary>
/// Real HashKey Chain implementation of <see cref="IEscrowChainService"/> backed
/// by the deployed <c>TradeEscrow</c> + <c>MockUSDC</c> contracts (Testnet, chain
/// doc §3/§4). A single platform/custodial wallet (the contract owner/deployer)
/// signs every transaction: it holds the settlement token, funds the escrow per
/// trade, and — as arbiter/owner — releases to the real seller or refunds itself.
/// Finality is only reported after the receipt status is confirmed successful on
/// the server (spec §4/§17). Produces real transaction hashes + Explorer links.
/// </summary>
public sealed class HashKeyEscrowChainService : IEscrowChainService
{
    // A trade may only be funded once on-chain. App trade ids can repeat across
    // process restarts (e.g. the in-memory demo db resets to 1), which would
    // collide with an escrow already funded on a prior run. Namespacing the
    // on-chain id with a per-process prefix keeps it stable within a run yet
    // unique across restarts, while staying deterministic for release/refund.
    private static readonly BigInteger RunPrefix =
        new BigInteger((uint)(DateTimeOffset.UtcNow.ToUnixTimeSeconds() & 0xFFFFFFFF)) << 32;

    private static readonly BigInteger MaxUint256 =
        BigInteger.Pow(2, 256) - 1;

    private readonly BlockchainOptions _options;
    private readonly ILogger<HashKeyEscrowChainService> _logger;
    private readonly Web3 _web3;
    private readonly Account _account;
    private readonly string _escrow;
    private readonly string _token;

    public HashKeyEscrowChainService(IOptions<BlockchainOptions> options, ILogger<HashKeyEscrowChainService> logger)
    {
        _options = options.Value;
        _logger = logger;

        if (string.IsNullOrWhiteSpace(_options.RpcUrl))
            throw new InvalidOperationException("Blockchain:RpcUrl is required for a non-demo environment.");
        if (string.IsNullOrWhiteSpace(_options.SignerPrivateKey))
            throw new InvalidOperationException("Blockchain:SignerPrivateKey is required for a non-demo environment.");
        if (string.IsNullOrWhiteSpace(_options.EscrowContractAddress))
            throw new InvalidOperationException("Blockchain:EscrowContractAddress is required for a non-demo environment.");
        if (string.IsNullOrWhiteSpace(_options.TokenContractAddress))
            throw new InvalidOperationException("Blockchain:TokenContractAddress is required for a non-demo environment.");

        _account = new Account(_options.SignerPrivateKey, _options.ChainId);
        _web3 = new Web3(_account, _options.RpcUrl);
        // HashKey Chain testnet gas is priced legacy-style; avoid EIP-1559 fields
        // that some nodes reject.
        _web3.TransactionManager.UseLegacyAsDefault = true;
        _escrow = _options.EscrowContractAddress!;
        _token = _options.TokenContractAddress!;
    }

    public bool IsMock => false;

    private static BigInteger OnChainId(int tradeId) => RunPrefix + (uint)tradeId;

    private BigInteger ToUnits(decimal amount) => Web3.Convert.ToWei(amount, _options.TokenDecimals);
    private decimal FromUnits(BigInteger units) => Web3.Convert.FromWei(units, _options.TokenDecimals);

    public async Task<EscrowState> GetEscrowStateAsync(int tradeId, CancellationToken ct = default)
    {
        try
        {
            var q = _web3.Eth.GetContractQueryHandler<GetEscrowFunction>();
            var e = await q.QueryDeserializingToObjectAsync<GetEscrowOutputDTO>(
                new GetEscrowFunction { TradeId = OnChainId(tradeId) }, _escrow);
            var exists = e.Status != 0;
            return new EscrowState(
                Exists: exists,
                IsFunded: e.Status == 1,
                IsReleased: e.Status == 2,
                IsRefunded: e.Status == 3,
                FundedAmount: FromUnits(e.Amount),
                TokenAddress: exists ? e.Token : null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "On-chain escrow state read failed for trade {TradeId}.", tradeId);
            return new EscrowState(false, false, false, false, 0m, null);
        }
    }

    public async Task<GasEstimate> EstimateAsync(string operation, CancellationToken ct = default)
    {
        try
        {
            var price = await _web3.Eth.GasPrice.SendRequestAsync();
            const long gasLimit = 150_000;
            var total = Web3.Convert.FromWei(price.Value * gasLimit);
            return new GasEstimate(gasLimit, price.Value.ToString(), total, _options.NativeCurrencySymbol);
        }
        catch
        {
            return new GasEstimate(150_000, null, null, _options.NativeCurrencySymbol);
        }
    }

    public async Task<ChainTxResult> FundAsync(int tradeId, string buyerWallet, string sellerWallet, decimal amount, string token, CancellationToken ct = default)
    {
        try
        {
            var units = ToUnits(amount);
            await EnsureTokenBalanceAsync(units, ct);
            await EnsureAllowanceAsync(units, ct);

            var handler = _web3.Eth.GetContractTransactionHandler<FundFunction>();
            var receipt = await handler.SendRequestAndWaitForReceiptAsync(_escrow, new FundFunction
            {
                TradeId = OnChainId(tradeId),
                Seller = sellerWallet,
                Token = _token,
                Amount = units
            }, ct);

            return ToResult("Fund", receipt, _account.Address, _escrow);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "On-chain fund failed for trade {TradeId}.", tradeId);
            return Fail("Fund", ex.Message);
        }
    }

    public async Task<ChainTxResult> ReleaseAsync(int tradeId, string sellerWallet, decimal amount, CancellationToken ct = default)
    {
        try
        {
            var handler = _web3.Eth.GetContractTransactionHandler<ReleaseFunction>();
            var receipt = await handler.SendRequestAndWaitForReceiptAsync(_escrow,
                new ReleaseFunction { TradeId = OnChainId(tradeId) }, ct);
            return ToResult("Release", receipt, _escrow, sellerWallet);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "On-chain release failed for trade {TradeId}.", tradeId);
            return Fail("Release", ex.Message);
        }
    }

    public async Task<ChainTxResult> RefundAsync(int tradeId, string buyerWallet, CancellationToken ct = default)
    {
        try
        {
            var handler = _web3.Eth.GetContractTransactionHandler<RefundFunction>();
            var receipt = await handler.SendRequestAndWaitForReceiptAsync(_escrow,
                new RefundFunction { TradeId = OnChainId(tradeId) }, ct);
            return ToResult("Refund", receipt, _escrow, _account.Address);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "On-chain refund failed for trade {TradeId}.", tradeId);
            return Fail("Refund", ex.Message);
        }
    }

    public async Task<ChainTxResult> RegisterHashesAsync(int tradeId, IReadOnlyDictionary<string, string> hashes, CancellationToken ct = default)
    {
        try
        {
            var keccak = new Sha3Keccack();
            var keys = new List<byte[]>();
            var values = new List<byte[]>();
            foreach (var kv in hashes)
            {
                keys.Add(keccak.CalculateHash(System.Text.Encoding.UTF8.GetBytes(kv.Key)));
                values.Add(ToBytes32(kv.Value));
            }

            var handler = _web3.Eth.GetContractTransactionHandler<AnchorHashesFunction>();
            var receipt = await handler.SendRequestAndWaitForReceiptAsync(_escrow, new AnchorHashesFunction
            {
                TradeId = OnChainId(tradeId),
                Keys = keys,
                Values = values
            }, ct);
            return ToResult("RegisterHashes", receipt, _account.Address, _escrow);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "On-chain hash anchoring failed for trade {TradeId}.", tradeId);
            return Fail("RegisterHashes", ex.Message);
        }
    }

    // -- helpers --------------------------------------------------------------

    private async Task EnsureTokenBalanceAsync(BigInteger required, CancellationToken ct)
    {
        var q = _web3.Eth.GetContractQueryHandler<BalanceOfFunction>();
        var balance = await q.QueryAsync<BigInteger>(_token, new BalanceOfFunction { Owner = _account.Address });
        if (balance >= required) return;

        // MockUSDC owner-mint a generous batch so subsequent funds need no mint.
        var mintAmount = BigInteger.Max(required - balance, ToUnits(1_000_000m));
        _logger.LogInformation("Minting {Amount} test-token units to custodial wallet.", mintAmount);
        var handler = _web3.Eth.GetContractTransactionHandler<MintFunction>();
        await handler.SendRequestAndWaitForReceiptAsync(_token, new MintFunction
        {
            To = _account.Address,
            Amount = mintAmount
        }, ct);
    }

    private async Task EnsureAllowanceAsync(BigInteger required, CancellationToken ct)
    {
        var q = _web3.Eth.GetContractQueryHandler<AllowanceFunction>();
        var allowance = await q.QueryAsync<BigInteger>(_token, new AllowanceFunction
        {
            Owner = _account.Address,
            Spender = _escrow
        });
        if (allowance >= required) return;

        _logger.LogInformation("Approving escrow to spend the custodial token balance.");
        var handler = _web3.Eth.GetContractTransactionHandler<ApproveFunction>();
        await handler.SendRequestAndWaitForReceiptAsync(_token, new ApproveFunction
        {
            Spender = _escrow,
            Amount = MaxUint256
        }, ct);
    }

    private ChainTxResult ToResult(string op, Nethereum.RPC.Eth.DTOs.TransactionReceipt receipt, string? from, string? to)
    {
        var success = receipt.Status?.Value == 1;
        return new ChainTxResult(
            Success: success,
            TransactionHash: receipt.TransactionHash,
            BlockNumber: (long)receipt.BlockNumber.Value,
            FromAddress: from,
            ToAddress: to,
            ContractAddress: _escrow,
            ChainId: _options.ChainId,
            GasUsed: (long)receipt.GasUsed.Value,
            Status: success ? "Success" : "Failed",
            ExplorerUrl: _options.TransactionUrl(receipt.TransactionHash),
            Error: success ? null : $"{op} transaction reverted on-chain.");
    }

    private ChainTxResult Fail(string op, string error) =>
        new(false, null, null, null, null, _escrow, _options.ChainId, null, "Failed", null, $"{op} failed: {error}");

    private static byte[] ToBytes32(string hexOrText)
    {
        var buf = new byte[32];
        try
        {
            var bytes = hexOrText.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? hexOrText.HexToByteArray()
                : new Sha3Keccack().CalculateHash(System.Text.Encoding.UTF8.GetBytes(hexOrText));
            Array.Copy(bytes, 0, buf, 0, Math.Min(32, bytes.Length));
        }
        catch
        {
            buf = new Sha3Keccack().CalculateHash(System.Text.Encoding.UTF8.GetBytes(hexOrText));
        }
        return buf;
    }

    // -- contract function / output definitions -------------------------------

    [Function("fund")]
    private sealed class FundFunction : FunctionMessage
    {
        [Parameter("uint256", "tradeId", 1)] public BigInteger TradeId { get; set; }
        [Parameter("address", "seller", 2)] public string Seller { get; set; } = "";
        [Parameter("address", "token", 3)] public string Token { get; set; } = "";
        [Parameter("uint256", "amount", 4)] public BigInteger Amount { get; set; }
    }

    [Function("release")]
    private sealed class ReleaseFunction : FunctionMessage
    {
        [Parameter("uint256", "tradeId", 1)] public BigInteger TradeId { get; set; }
    }

    [Function("refund")]
    private sealed class RefundFunction : FunctionMessage
    {
        [Parameter("uint256", "tradeId", 1)] public BigInteger TradeId { get; set; }
    }

    [Function("anchorHashes")]
    private sealed class AnchorHashesFunction : FunctionMessage
    {
        [Parameter("uint256", "tradeId", 1)] public BigInteger TradeId { get; set; }
        [Parameter("bytes32[]", "keys", 2)] public List<byte[]> Keys { get; set; } = new();
        [Parameter("bytes32[]", "values", 3)] public List<byte[]> Values { get; set; } = new();
    }

    [Function("getEscrow", typeof(GetEscrowOutputDTO))]
    private sealed class GetEscrowFunction : FunctionMessage
    {
        [Parameter("uint256", "tradeId", 1)] public BigInteger TradeId { get; set; }
    }

    [FunctionOutput]
    private sealed class GetEscrowOutputDTO : IFunctionOutputDTO
    {
        [Parameter("address", "buyer", 1)] public string Buyer { get; set; } = "";
        [Parameter("address", "seller", 2)] public string Seller { get; set; } = "";
        [Parameter("address", "token", 3)] public string Token { get; set; } = "";
        [Parameter("uint256", "amount", 4)] public BigInteger Amount { get; set; }
        [Parameter("uint8", "status", 5)] public byte Status { get; set; }
    }

    [Function("balanceOf", "uint256")]
    private sealed class BalanceOfFunction : FunctionMessage
    {
        [Parameter("address", "account", 1)] public string Owner { get; set; } = "";
    }

    [Function("allowance", "uint256")]
    private sealed class AllowanceFunction : FunctionMessage
    {
        [Parameter("address", "owner", 1)] public string Owner { get; set; } = "";
        [Parameter("address", "spender", 2)] public string Spender { get; set; } = "";
    }

    [Function("approve", "bool")]
    private sealed class ApproveFunction : FunctionMessage
    {
        [Parameter("address", "spender", 1)] public string Spender { get; set; } = "";
        [Parameter("uint256", "amount", 2)] public BigInteger Amount { get; set; }
    }

    [Function("mint")]
    private sealed class MintFunction : FunctionMessage
    {
        [Parameter("address", "to", 1)] public string To { get; set; } = "";
        [Parameter("uint256", "amount", 2)] public BigInteger Amount { get; set; }
    }
}

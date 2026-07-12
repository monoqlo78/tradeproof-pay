using System.ComponentModel.DataAnnotations;

namespace HashKeyChain.Configuration;

/// <summary>
/// Blockchain environment. DemoMode uses an in-process mock escrow; Testnet and
/// Mainnet target real HashKey Chain networks (chain doc §3/§6/§15).
/// </summary>
public enum BlockchainEnvironment
{
    DemoMode,
    Testnet,
    Mainnet
}

/// <summary>
/// Strongly-typed blockchain configuration (chain doc §3). Values MUST come from
/// configuration — Chain ID / RPC / Explorer / contract addresses are never
/// hardcoded in controllers, Razor or TypeScript. Validated at startup.
/// </summary>
public sealed class BlockchainOptions
{
    public const string SectionName = "Blockchain";

    public BlockchainEnvironment Environment { get; set; } = BlockchainEnvironment.DemoMode;

    public string? RpcUrl { get; set; }

    public int ChainId { get; set; }

    [Required]
    public string NativeCurrencySymbol { get; set; } = "HSK";

    public string? ExplorerBaseUrl { get; set; }

    public string? EscrowContractAddress { get; set; }

    public string? TokenContractAddress { get; set; }

    /// <summary>Number of decimals of the settlement token (MockUSDC = 6).</summary>
    public int TokenDecimals { get; set; } = 6;

    /// <summary>Private key of the custodial/arbiter wallet that signs on-chain
    /// escrow operations in Testnet/Mainnet. Never committed — supplied via
    /// user-secrets locally or an App Setting in Azure. Ignored in DemoMode.</summary>
    public string? SignerPrivateKey { get; set; }

    [Range(1, 64)]
    public int RequiredConfirmations { get; set; } = 1;

    public bool IsDemoMode => Environment == BlockchainEnvironment.DemoMode;

    /// <summary>Explorer URL for a transaction hash, or null when unavailable.</summary>
    public string? TransactionUrl(string? txHash) =>
        string.IsNullOrWhiteSpace(ExplorerBaseUrl) || string.IsNullOrWhiteSpace(txHash)
            ? null
            : $"{ExplorerBaseUrl!.TrimEnd('/')}/tx/{txHash}";

    /// <summary>Explorer URL for an address, or null when unavailable.</summary>
    public string? AddressUrl(string? address) =>
        string.IsNullOrWhiteSpace(ExplorerBaseUrl) || string.IsNullOrWhiteSpace(address)
            ? null
            : $"{ExplorerBaseUrl!.TrimEnd('/')}/address/{address}";
}

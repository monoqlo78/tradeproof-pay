using Microsoft.Extensions.Options;

namespace HashKeyChain.Configuration;

/// <summary>
/// Validates <see cref="BlockchainOptions"/> at startup (chain doc §3). Testnet
/// and Mainnet require RPC/Chain ID/Explorer to be present and consistent; the
/// canonical HashKey Chain IDs are enforced (Testnet 133 / Mainnet 177). DemoMode
/// requires none of these.
/// </summary>
public sealed class BlockchainOptionsValidator : IValidateOptions<BlockchainOptions>
{
    public const int TestnetChainId = 133;
    public const int MainnetChainId = 177;

    public ValidateOptionsResult Validate(string? name, BlockchainOptions options)
    {
        var errors = new List<string>();

        if (options.Environment == BlockchainEnvironment.DemoMode)
            return ValidateOptionsResult.Success;

        if (string.IsNullOrWhiteSpace(options.RpcUrl))
            errors.Add("Blockchain:RpcUrl is required for Testnet/Mainnet.");

        if (string.IsNullOrWhiteSpace(options.ExplorerBaseUrl))
            errors.Add("Blockchain:ExplorerBaseUrl is required for Testnet/Mainnet.");

        var expectedChainId = options.Environment == BlockchainEnvironment.Testnet
            ? TestnetChainId
            : MainnetChainId;

        if (options.ChainId != expectedChainId)
            errors.Add($"Blockchain:ChainId must be {expectedChainId} for {options.Environment} " +
                       $"(configured {options.ChainId}).");

        if (options.Environment == BlockchainEnvironment.Mainnet &&
            string.Equals(options.TokenContractAddress, options.EscrowContractAddress, StringComparison.OrdinalIgnoreCase))
            errors.Add("Blockchain: token and escrow contract addresses must differ on Mainnet.");

        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }
}

// Deploys the TradeProof Pay escrow (and a MockUSDC settlement token if none is
// supplied) to the selected network. Prints addresses and Explorer links.
//
// Usage:
//   npx hardhat run scripts/deploy.js --network hashkeyTestnet
//
// Requires PRIVATE_KEY in .env, funded with test HSK from https://faucet.hsk.xyz
const hre = require("hardhat");

const EXPLORERS = {
  133: "https://testnet-explorer.hsk.xyz",
  177: "https://hashkey.blockscout.com",
};

async function main() {
  const net = await hre.ethers.provider.getNetwork();
  const chainId = Number(net.chainId);
  const explorer = EXPLORERS[chainId];

  const [deployer] = await hre.ethers.getSigners();
  if (!deployer) {
    throw new Error(
      "No deployer account. Set PRIVATE_KEY in chain/.env (funded with test HSK from https://faucet.hsk.xyz)."
    );
  }

  const balance = await hre.ethers.provider.getBalance(deployer.address);
  console.log("Network      :", hre.network.name, `(chainId ${chainId})`);
  console.log("Deployer     :", deployer.address);
  console.log("Balance      :", hre.ethers.formatEther(balance), "HSK");
  if (balance === 0n && chainId !== 31337) {
    throw new Error(
      "Deployer balance is 0. Fund it with test HSK from https://faucet.hsk.xyz before deploying."
    );
  }

  // 1) Settlement token: reuse TOKEN_ADDRESS or deploy a fresh MockUSDC.
  let tokenAddress = process.env.TOKEN_ADDRESS?.trim();
  if (!tokenAddress) {
    console.log("\nDeploying MockUSDC (test-only, no monetary value)...");
    const MockUSDC = await hre.ethers.getContractFactory("MockUSDC");
    const token = await MockUSDC.deploy(deployer.address);
    await token.waitForDeployment();
    tokenAddress = await token.getAddress();
    console.log("MockUSDC     :", tokenAddress);
  } else {
    console.log("\nUsing existing settlement token:", tokenAddress);
  }

  // 2) Escrow, owned by the deployer (platform arbiter).
  console.log("\nDeploying TradeEscrow...");
  const TradeEscrow = await hre.ethers.getContractFactory("TradeEscrow");
  const escrow = await TradeEscrow.deploy(deployer.address);
  await escrow.waitForDeployment();
  const escrowAddress = await escrow.getAddress();
  console.log("TradeEscrow  :", escrowAddress);

  console.log("\n=== Deployment summary ===");
  console.log(JSON.stringify({ chainId, tokenAddress, escrowAddress, owner: deployer.address }, null, 2));

  if (explorer) {
    console.log("\nExplorer:");
    console.log("  Escrow :", `${explorer}/address/${escrowAddress}`);
    console.log("  Token  :", `${explorer}/address/${tokenAddress}`);
  }

  console.log("\nNext: set these in HashKeyChain/appsettings (Blockchain section):");
  console.log(
    JSON.stringify(
      {
        Environment: "Testnet",
        RpcUrl: hre.network.config.url,
        ChainId: chainId,
        ExplorerBaseUrl: explorer || "",
        EscrowContractAddress: escrowAddress,
        TokenContractAddress: tokenAddress,
      },
      null,
      2
    )
  );
}

main().catch((err) => {
  console.error(err);
  process.exitCode = 1;
});

require("@nomicfoundation/hardhat-toolbox");
require("dotenv").config();

// Never hardcode chain settings, RPC URLs or private keys (HashKey reference,
// implementation principles). They are read from the environment / .env.
const {
  HASHKEY_TESTNET_RPC,
  HASHKEY_MAINNET_RPC,
  PRIVATE_KEY,
} = process.env;

const accounts = PRIVATE_KEY ? [PRIVATE_KEY] : [];

/** @type {import('hardhat/config').HardhatUserConfig} */
module.exports = {
  solidity: {
    version: "0.8.24",
    settings: {
      optimizer: { enabled: true, runs: 200 },
    },
  },
  networks: {
    // Local in-process network for unit tests.
    hardhat: {},
    // HashKey Chain Testnet (Chain ID 133). Verify RPC/Chain ID before deploy.
    hashkeyTestnet: {
      url: HASHKEY_TESTNET_RPC || "https://testnet.hsk.xyz",
      chainId: 133,
      accounts,
    },
    // HashKey Chain Mainnet (Chain ID 177). Do NOT deploy without explicit approval.
    hashkeyMainnet: {
      url: HASHKEY_MAINNET_RPC || "https://mainnet.hsk.xyz",
      chainId: 177,
      accounts,
    },
  },
};

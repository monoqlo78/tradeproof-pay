# TradeProof Pay — On-chain contracts (HashKey Chain)

Hardhat project for the escrow contract that settles trades on HashKey Chain.

## Contracts
- **`TradeEscrow.sol`** — holds an ERC-20 amount per off-chain `tradeId`.
  - `fund(tradeId, seller, token, amount)` — buyer funds (must `approve` first).
  - `release(tradeId)` — pay the seller. Callable by `owner` (platform arbiter) or the buyer.
  - `refund(tradeId)` — refund the buyer. Callable by `owner` or the buyer.
  - `anchorHashes(tradeId, keys[], values[])` — anchor document/verdict/approval hashes (owner only).
  - Invariants: fund once, release once, refund once; released ⇒ never refundable (and vice versa). `ReentrancyGuard` + `SafeERC20`.
- **`test/MockUSDC.sol`** — 6-decimal, owner-mintable test token. **No monetary value. Testnet/local only.**

## Prerequisites
- Node.js + npm. `npm install` in this folder.
- For deploy: a deployer wallet private key in `.env` (copy from `.env.example`), funded with
  test HSK from the faucet: https://faucet.hsk.xyz . **Never commit `.env`.**

## Commands
```bash
npm run compile        # hardhat compile
npm test               # run the contract test suite
npm run deploy:testnet # deploy MockUSDC + TradeEscrow to HashKey Testnet
```

## Networks (see hardhat.config.js)
| Network | chainId | RPC | Explorer |
|---|---|---|---|
| hashkeyTestnet | 133 | https://testnet.hsk.xyz | https://testnet-explorer.hsk.xyz |
| hashkeyMainnet | 177 | https://mainnet.hsk.xyz | https://hashkey.blockscout.com |

> Mainnet deploys require explicit approval. Verify RPC / chainId / addresses before every deploy.

## Deployed (Testnet, 2026-07-12)
See `deployments/hashkeyTestnet.json`.
- TradeEscrow: `0x32cFF91E852ab80aD29fCE1AA63B7B40342B44c1`
- MockUSDC:    `0xFff35Fd2A98252F3dD5ad76D4b3e425993C6153c`

## Windows-on-ARM note
Hardhat's native modules (`@nomicfoundation/edr`, `solidity-analyzer`) currently ship a
`win32-x64-msvc` binary but no `win32-arm64-msvc` binary for this version. On a Windows-ARM
machine, run Hardhat under an **x64 Node** (Windows emulates x64), e.g.:
```powershell
& <path-to-x64-node>\node.exe node_modules\hardhat\internal\cli\cli.js test
```
`solidity-analyzer` alone can also use the 0.1.1 arm64 `.node` dropped next to its `index.js`
(that gets `compile` working on native arm64), but `edr` (the in-process EVM used by tests and
the deploy provider) requires the x64 runtime path above.

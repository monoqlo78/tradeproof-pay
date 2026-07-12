# HashKey Chain Reference

> 一次情報は公式ドキュメント (https://docs.hashkeychain.net/) を参照すること。
> コントラクトアドレス・RPC URL・ネットワーク設定・利用可能トークンは **デプロイ直前に必ず再確認**する。
>
> **最終確認日 (Last verified): 2026-07-12**
> 備考: 本ファイルの値は提示された公式仕様資料に基づき記録。ライブ再確認はデプロイ直前に実施すること。

## 公式 URL 一覧
- Docs Top: https://docs.hashkeychain.net/
- Developer QuickStart: https://docs.hashkeychain.net/docs/Developer-QuickStart
- Network Info: https://docs.hashkeychain.net/docs/Build-on-HashKey-Chain/network-info
- Token Contracts: https://docs.hashkeychain.net/docs/Build-on-HashKey-Chain/Token-Contracts
- System Contract Addresses: https://docs.hashkeychain.net/docs/Build-on-HashKey-Chain/Contract-Addresses
- Explorer: https://docs.hashkeychain.net/docs/Build-on-HashKey-Chain/Tools/Explorer
- Wallet: https://docs.hashkeychain.net/docs/Build-on-HashKey-Chain/Tools/Wallet
- Faucet: https://docs.hashkeychain.net/docs/Build-on-HashKey-Chain/Tools/Faucet
- Safe: https://docs.hashkeychain.net/docs/Build-on-HashKey-Chain/Tools/Safe
- Bridge: https://docs.hashkeychain.net/docs/Build-on-HashKey-Chain/Tools/Bridges
- Fee: https://docs.hashkeychain.net/docs/Build-on-HashKey-Chain/Fee
- RPC / Node Provider: https://docs.hashkeychain.net/docs/Build-on-HashKey-Chain/RPC-Node-Provider
- KYC: https://docs.hashkeychain.net/docs/Build-on-HashKey-Chain/Tools/KYC

## Testnet 設定 (開発・デモの基本環境)
| 項目 | 値 |
|---|---|
| Network Name | HashKey Chain Testnet |
| RPC URL | https://testnet.hsk.xyz |
| Chain ID | 133 |
| Native Token | HSK |
| Explorer | https://testnet-explorer.hsk.xyz |
| Faucet | https://faucet.hsk.xyz |
| Testnet Bridge | https://testnet-bridge.hashkeychain.net |
| Testnet Safe | https://testnet-safe.hsk.xyz |

- Explorer Tx: `https://testnet-explorer.hsk.xyz/tx/{transactionHash}`
- Explorer Address: `https://testnet-explorer.hsk.xyz/address/{contractAddress}`

## Mainnet 設定 (明示承認なしにデプロイ禁止)
| 項目 | 値 |
|---|---|
| Network Name | HashKey Chain |
| RPC URL | https://mainnet.hsk.xyz |
| Chain ID | 177 |
| Native Token | HSK |
| Explorer | https://hashkey.blockscout.com |
| Mainnet Safe | https://multisig.hashkeychain.net |

## トークン
- **MockUSDC** (Testnet 専用): ERC-20, `decimals = 6`, Owner/Faucet Role のみ Mint 可。金銭的価値なし。Mainnet 使用禁止。
  - 表示 (JP): 「MockUSDC はハッカソンのテスト専用トークンであり、金銭的価値はありません。」
  - 表示 (EN): "MockUSDC is a test-only token for this hackathon and has no monetary value."
- Mainnet で USDT/USDC を使う場合は Token Contracts ページと Explorer で Chain ID + Contract Address を都度検証。記憶・古い README・第三者ブログからのコピー禁止。

## Fee (ガス代)
- L2 Execution Fee + L1 Security Fee で構成。固定ガス価格を画面表示しない。
- 実行前に gasLimit / maxFeePerGas / 推定合計 / HSK 残高を可能な範囲で見積り、「推定値」と明示。
  - (JP): 「表示されるネットワーク手数料は推定値です。実際の手数料はネットワーク状況により変動します。」
  - (EN): "The displayed network fee is an estimate. The final fee may vary depending on network conditions."

## 実装原則
- EVM 互換: Solidity / Hardhat / OpenZeppelin / ethers.js v6 / MetaMask。
- Chain ID / RPC / Explorer / Contract Address を Controller・Razor・TS へ**ハードコードしない**。`Blockchain` Options で読み取り起動時検証。
- Testnet と Mainnet の設定を混在させない。Chain ID を必ず検証。
- 秘密鍵を Web サーバー / App Service / ソースへ保存しない。買主が自分のウォレットで署名。
- **Receipt の status 成功をサーバー側で確認するまで Settled / Refunded にしない。**
- Explorer リンクを必ず提供。Mainnet デプロイ・実資産送金を自動実行しない。

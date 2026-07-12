# TradeProof Pay

> **Conditional international‑trade escrow settlement** — verify trade documents with **AI + deterministic rules**, gate every state change on a **human approval**, and settle on **HashKey Chain**.
>
> MVP built for the **HashKey Chain Horizon Hackathon Japan** (AI + DeFi track).

**Stack:** Blazor Server / .NET 10 · Entity Framework Core (Azure SQL / InMemory) · ASP.NET Core Localization (ja‑JP default / en‑US) · Azure OpenAI (gpt‑5.4) · Azure AI Document Intelligence · Solidity / Hardhat on HashKey Chain.

---

## 1. What it is

TradeProof Pay is a web app that safely intermediates payment for an international trade between a **buyer/importer**, a **seller/exporter**, and a **document verifier**, using an **escrow**. The demo is presented from the **buyer/importer (JP‑side) perspective**: the operating company is your *own* company, fixed through the Settings screen, and you only pick the *seller* per trade.

The happy path:

1. The buyer registers the trade terms (counterparty, amount, currency, transport mode, quantity, deadline, seller wallet) and an approver **locks the terms**.
2. The buyer **funds** the escrow.
3. The seller **submits** the shipping documents (Commercial Invoice / Packing List / B/L or AWB).
4. **AI extraction + a deterministic rule engine** cross‑check the documents against each other and against the trade terms.
5. A human **document verifier** approves or sends back.
6. A **buyer approver** signs the final payment and the escrow **settles** to the seller.
7. On expiry (and similar exceptions) the buyer is **refunded**.

**Separation of Duties** is enforced end‑to‑end — e.g. a verifier cannot sign settlement, and an operator cannot give final approval.

## 2. Features

- **Buyer‑centric dashboard** (`/`) and **trade list** (`/trades`) with per‑state, per‑role actions.
- **New trade** (`/trades/new`) — buyer is fixed to your own company; only the seller is selectable.
- **Trade detail** (`/trades/{id}`) — the full 18‑state workflow, document upload/versioning, verification result, audit trail.
- **Conversational AI agent** (`/agent`) — a chat assistant powered by Azure OpenAI (gpt‑5.4) with **tool/function calling** that can walk you through a trade and take actions.
- **Real OCR** — uploaded documents are analysed with **Azure AI Document Intelligence** and normalised with gpt‑5.4 (falls back to a deterministic mock when AI keys are not configured).
- **Master data screens** — companies (`/master/companies`) and users (`/master/users`).
- **Settings** (`/settings`) — configure your *own* (buyer) company and its funding wallet. Persisted to a local JSON file, **not** the database (no schema change).
- **Localization** — one‑click Japanese ⇄ English; UI strings live in `.resx`.
- **Audit trail** — every significant action is written to an append‑only `AuditEntry` (before/after state, tx hash).

Roles & separation of duties:

| Role | Can | Cannot |
| --- | --- | --- |
| `BuyerOperator` | draft trades, enter terms, request approval, run analysis, submit received docs | final payment approval / signing |
| `BuyerApprover` | approve terms, fund, approve payment, sign, refund | finalize document verification |
| `Seller` | view own trades, submit / resubmit docs | approve / settle |
| `TradeVerifier` | finalize verification / send back / manual review | sign payment |
| `Administrator` | manage demo data & settings (holds all buyer‑side roles) | delete audit records |

> In the buyer‑centric demo, the persona switcher only exposes the buyer‑side roles (`BuyerOperator`, `BuyerApprover`, `Administrator`); no login is required — switch users/roles from the bar at the top of the screen.

## 3. Trade lifecycle (18 states)

```
Draft → PendingTradeApproval → AwaitingFunding → Funded → AwaitingDocuments
      → Analyzing → (ReadyForVerification | ManualReview | Blocked | DocumentsRejected)
      → ReadyForApproval → Approved → SettlementPending → Settled
```

- Exceptions: from any funded, pre‑settlement state → `Expired → RefundPending → Refunded`.
- `DocumentsRejected → AwaitingDocuments` (seller resubmits → re‑analysis).
- Terminal states: `Settled` / `Refunded` / `Cancelled`.
- All transitions go through `ITradeStateMachine`; UI/services never mutate `Status` directly.

## 4. Verification rule engine

Deterministic rules cross‑check documents and trade terms. **AI risk scoring can never override a hard‑rule violation.**

- **Hard rules (violation → `Blocked`):** required documents present, invoice amount == trade amount, currency match, seller wallet unchanged, quantity match, container‑number consistency, shipment date ≤ deadline.
- **Soft rules (violation → `ManualReview`):** confidence ≥ 0.75, declared document type == detected type, seller name match.

Decision: any hard violation → **Blocked**; no hard but a soft violation → **ManualReview**; all pass → **Pass**.

## 5. Blockchain

The on‑chain contracts live under [`chain/`](chain) (Hardhat):

- **`TradeEscrow.sol`** — holds an ERC‑20 amount per off‑chain `tradeId`; `fund` / `release` / `refund` / `anchorHashes`, with `ReentrancyGuard` + `SafeERC20`. Invariants: fund once, release once, refund once; released ⇒ never refundable.
- **`MockUSDC.sol`** — 6‑decimal, owner‑mintable **test** token. No monetary value; testnet/local only.

Deployed to **HashKey Chain Testnet** (chainId 133, RPC `https://testnet.hsk.xyz`, native `HSK`) — see `chain/deployments/hashkeyTestnet.json`.

The web app itself settles through a swappable `IEscrowChainService`; the default `DemoMode` uses an in‑process mock escrow so the acceptance criteria can be demonstrated without a wallet. Settlement/refund only finalize **after receipt confirmation**; double‑settle and double‑refund are rejected in both the service layer and the mock chain.

## 6. Run it locally (DemoMode)

DemoMode uses a mock in‑process escrow and (when AI keys are absent) a mock analyzer, so it runs with no external services. Real chain / Azure AI are drop‑in via interfaces.

**Prerequisites**
- .NET SDK 10.0.300+
- Only if connecting to Azure SQL: `az login` for the target account.

**Start**

```powershell
cd HashKeyChain          # folder containing the web project
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet run
```

- Listens on http://localhost:5000 by default.
- On startup, **2 demo companies and 5 role users** are seeded idempotently.
- Switch users/roles from the **DemoUserBar** at the top — no login needed.

**Optional: seed end‑to‑end demo trades**

```powershell
dotnet run -- --run-demo-flow
```

| Scenario | Result |
| --- | --- |
| Clean (all documents match) | **Settled** |
| Invoice amount mismatch (hard‑rule violation) | **Blocked** |
| Low confidence (soft failure) | **ManualReview → human check → Settled** |
| Expired after funding | **Refunded** |

## 7. Configuration

Copy the example config and fill in your own values:

```powershell
copy HashKeyChain\appsettings.Development.json.example HashKeyChain\appsettings.Development.json
```

`appsettings.Development.json`:

```json
{
  "Database": {
    "Provider": "SqlServer",
    "ConnectionString": "Server=tcp:<your-server>.database.windows.net,1433;Database=<your-db>;Authentication=Active Directory Default;Encrypt=True;TrustServerCertificate=False;Connect Timeout=30;",
    "ApplyBootstrap": false
  },
  "Agent": { "Enabled": true },
  "DocumentIntelligence": { "Enabled": true }
}
```

| `Database.Provider` | Use |
| --- | --- |
| `SqlServer` | connect to Azure SQL (Entra auth via `Active Directory Default`) |
| `InMemory` | tests |
| `None` | UI only, no persistence |

**AI keys** are read from .NET user‑secrets / environment variables — never commit them:

```powershell
cd HashKeyChain
dotnet user-secrets set "Agent:ApiKey" "<azure-openai-key>"
dotnet user-secrets set "DocumentIntelligence:ApiKey" "<doc-intelligence-key>"
```

Endpoints/deployment names live in `appsettings.json` (`Agent`, `DocumentIntelligence`, `Blockchain`). When keys are absent the app automatically falls back to the deterministic mocks.

### 🔒 Data safety (strict)

- The app uses a dedicated **`hashkeychain` schema only**. It never touches other schemas/tables/data (`dbo`, etc.).
- Startup seeding (companies/users) is **additive and idempotent** — no updates or deletes of existing rows.
- No `EnsureCreated` / `DROP` against a real DB (`ApplyBootstrap: false`); schema is assumed pre‑provisioned.
- Keep the real connection string and all keys out of git (user‑secrets / environment / App Service settings).

## 8. Tests

```powershell
cd HashKeyChain
dotnet test
```

Integration tests force the `InMemory` provider via `TestAppFactory` and never connect to Azure SQL.

## 9. Architecture

```
HashKeyChain/                          Web project (Blazor Server, .NET 10)
  Domain/          Trade, Company, AppUser, TradeDocument, VerificationRun, ChainTransaction, AuditEntry …
  Services/
    Trades/        TradeStateMachine, TradeService, EscrowService, AuditWriter
    Security/      TradePermissions (role policy), CurrentUserContext
    Documents/     DocumentService (versioning + SHA-256), storage
    Analysis/      DocumentAnalysis (Azure DI + gpt-5.4 / Mock), TradeRuleEngine
    Verification/  VerificationService
    Settlement/    SettlementService, RefundService (receipt-confirmed, double-safe)
    Blockchain/    IEscrowChainService, MockEscrowChainService
    Agent/         ChatClientProvider, TradeAgentService (gpt-5.4 tool calling)
    Master/        MasterDataService
    Settings/      OrgSettingsService (own company + wallet, JSON file)
    Demo/          DemoDataSeeder, DemoScenarioRunner
  Components/
    Pages/         Home, Trades, NewTrade, TradeDetail, Agent, MasterCompanies, MasterUsers, Settings
    Shared/        DemoUserBar
  Data/            AppDbContext (hashkeychain schema), provider switch
HashKeyChain.Tests/                    xUnit tests
chain/                                 Hardhat contracts (TradeEscrow, MockUSDC)
docs/HASHKEY_CHAIN_REFERENCE.md        HashKey Chain network reference
```

## 10. Localization

ASP.NET Core Localization switches **ja‑JP (default) / en‑US**. UI strings live in `Resources/SharedResource.*.resx`; internal enum values, JSON keys, wallet addresses, tx hashes and currency codes stay in English (display only is localized). Language choice never affects settlement, business decisions, or permissions.

## License

Hackathon prototype. `MockUSDC` is a valueless test token for testnet/local use only.

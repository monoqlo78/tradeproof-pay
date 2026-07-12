using System.Text.Json;
using HashKeyChain.Data;
using HashKeyChain.Domain;
using HashKeyChain.Localization;
using HashKeyChain.Services.Master;
using HashKeyChain.Services.Security;
using HashKeyChain.Services.Settlement;
using HashKeyChain.Services.Trades;
using HashKeyChain.Services.Verification;
using Microsoft.EntityFrameworkCore;
using OpenAI.Chat;

namespace HashKeyChain.Services.Agent;

/// <summary>
/// Conversational operations assistant. The user "presses the agent" and drives the
/// trade lifecycle by chatting: the agent reads state and performs actions through
/// the existing domain services via LLM tool/function calling. Every state-changing
/// tool is permission-guarded through <see cref="ICurrentUserContext"/> (the same
/// separation-of-duties policy the UI enforces), and responses follow the current UI
/// language. Conversation state lives in the caller (the Blazor circuit) — no new
/// database tables are introduced, preserving the data-safety guarantees.
/// </summary>
public interface ITradeAgentService
{
    bool IsAvailable { get; }

    /// <summary>Seed a fresh conversation (adds the system prompt) and return it.</summary>
    List<ChatMessage> NewConversation();

    /// <summary>Append the user message, run the tool loop, and return the assistant reply.</summary>
    Task<string> ContinueAsync(List<ChatMessage> history, string userMessage, CancellationToken ct = default);
}

public sealed class TradeAgentService : ITradeAgentService
{
    private readonly IChatClientProvider _chat;
    private readonly ICurrentUserContext _user;
    private readonly IAgentLanguageContextProvider _language;
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly ITradeService _trades;
    private readonly IEscrowService _escrow;
    private readonly IVerificationService _verification;
    private readonly ISettlementService _settlement;
    private readonly IRefundService _refund;
    private readonly IMasterDataService _master;
    private readonly ILogger<TradeAgentService> _logger;
    private readonly IReadOnlyList<ChatTool> _tools;

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public TradeAgentService(
        IChatClientProvider chat,
        ICurrentUserContext user,
        IAgentLanguageContextProvider language,
        IDbContextFactory<AppDbContext> dbFactory,
        ITradeService trades,
        IEscrowService escrow,
        IVerificationService verification,
        ISettlementService settlement,
        IRefundService refund,
        IMasterDataService master,
        ILogger<TradeAgentService> logger)
    {
        _chat = chat;
        _user = user;
        _language = language;
        _dbFactory = dbFactory;
        _trades = trades;
        _escrow = escrow;
        _verification = verification;
        _settlement = settlement;
        _refund = refund;
        _master = master;
        _logger = logger;
        _tools = BuildTools();
    }

    public bool IsAvailable => _chat.IsAvailable;

    private string Actor => _user.User?.Email ?? "demo";

    public List<ChatMessage> NewConversation() => new() { new SystemChatMessage(BuildSystemPrompt()) };

    public async Task<string> ContinueAsync(List<ChatMessage> history, string userMessage, CancellationToken ct = default)
    {
        var client = _chat.GetChatClient();
        if (client is null)
            return "Agent is not configured. Set Azure OpenAI endpoint/key/deployment to enable the assistant.";

        if (history.Count == 0)
            history.Add(new SystemChatMessage(BuildSystemPrompt()));
        history.Add(new UserChatMessage(userMessage));

        var options = new ChatCompletionOptions();
        foreach (var tool in _tools)
            options.Tools.Add(tool);

        for (var iteration = 0; iteration < 8; iteration++)
        {
            ChatCompletion completion = await client.CompleteChatAsync(history, options, ct);

            if (completion.ToolCalls.Count > 0)
            {
                history.Add(new AssistantChatMessage(completion));
                foreach (var call in completion.ToolCalls)
                {
                    string result;
                    try
                    {
                        result = await DispatchAsync(call.FunctionName, call.FunctionArguments, ct);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Agent tool {Tool} failed.", call.FunctionName);
                        result = Json(new { error = ex.Message });
                    }
                    history.Add(new ToolChatMessage(call.Id, result));
                }
                continue;
            }

            history.Add(new AssistantChatMessage(completion));
            return completion.Content.Count > 0
                ? string.Concat(completion.Content.Select(c => c.Text))
                : string.Empty;
        }

        return "The assistant reached the maximum number of tool steps. Please refine your request.";
    }

    // ---- System prompt ---------------------------------------------------

    private string BuildSystemPrompt()
    {
        var role = _user.ActiveRole?.ToString() ?? "None";
        var held = _user.HeldRoles.Count > 0 ? string.Join(", ", _user.HeldRoles) : "None";
        var who = _user.User is { } u ? $"{u.DisplayName} <{u.Email}>" : "unauthenticated";

        return
            "You are the operations assistant for TradeProof Pay, a conditional international-trade escrow " +
            "payment system. You help the user drive the trade lifecycle by conversation, calling tools to read " +
            "state and perform actions.\n\n" +
            $"Current user: {who}. Active role: {role}. Held roles: {held}.\n\n" +
            "Trade lifecycle: Draft -> PendingApproval -> ConditionsApproved -> Funded -> (documents uploaded by " +
            "seller) -> analysis run -> verified/blocked/manual-review -> payment approved -> Settled; or Refunded " +
            "when expired/cancelled. Actions are role-gated (separation of duties): only a Buyer Operator/Admin " +
            "creates trades and requests approval; a Buyer Approver approves conditions, funds escrow, approves " +
            "payment, settles and refunds; a Trade Verifier verifies/rejects/raises manual review.\n\n" +
            "Rules:\n" +
            "- Always confirm the current state with get_trade before acting.\n" +
            "- If a tool returns an error (e.g. permission_denied), explain it plainly and suggest which role is " +
            "needed; do not retry the same action.\n" +
            "- Before irreversible actions (settle, refund, cancel_trade) briefly confirm intent with the user " +
            "unless they already clearly asked for it.\n" +
            "- Document upload requires a file and is done on the trade screen, not via chat; when documents are " +
            "missing, tell the user to upload them there, then you can run analysis.\n" +
            "- Be concise. Reference trades by their TradeReference and id.\n\n" +
            _language.GetSystemPromptDirective();
    }

    // ---- Tool dispatch ---------------------------------------------------

    private async Task<string> DispatchAsync(string name, BinaryData args, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(args.ToMemory().IsEmpty ? "{}" : args.ToString());
        var root = doc.RootElement;

        switch (name)
        {
            case "list_trades": return await ListTradesAsync(GetString(root, "status"), ct);
            case "get_trade": return await GetTradeAsync(GetInt(root, "tradeId") ?? 0, ct);
            case "list_companies": return await ListCompaniesAsync(ct);
            case "list_users": return await ListUsersAsync(ct);
            case "create_trade": return await CreateTradeAsync(root, ct);
            case "request_approval": return await ActAsync(TradeOperation.RequestApproval, () => _trades.RequestApprovalAsync(GetInt(root, "tradeId") ?? 0, Actor, ct));
            case "approve_conditions": return await ActAsync(TradeOperation.ApproveConditions, () => _trades.ApproveConditionsAsync(GetInt(root, "tradeId") ?? 0, Actor, GetString(root, "comment"), ct));
            case "fund_escrow": return await ActAsync(TradeOperation.FundEscrow, () => _escrow.FundAsync(GetInt(root, "tradeId") ?? 0, Actor, ct));
            case "run_analysis": return await RunAnalysisAsync(GetInt(root, "tradeId") ?? 0, ct);
            case "verify_documents": return await ActAsync(TradeOperation.VerifyDocuments, () => _verification.ConfirmAsync(GetInt(root, "tradeId") ?? 0, Actor, GetString(root, "comment"), ct));
            case "reject_documents": return await ActAsync(TradeOperation.RejectDocuments, () => _verification.RejectAsync(GetInt(root, "tradeId") ?? 0, Actor, GetString(root, "reason") ?? "Rejected via agent", ct));
            case "raise_manual_review": return await ActAsync(TradeOperation.RaiseManualReview, () => _verification.RaiseManualReviewAsync(GetInt(root, "tradeId") ?? 0, Actor, GetString(root, "comment"), ct));
            case "approve_payment": return await ActAsync(TradeOperation.ApprovePayment, () => _settlement.ApprovePaymentAsync(GetInt(root, "tradeId") ?? 0, Actor, GetString(root, "comment"), ct));
            case "settle": return await ActAsync(TradeOperation.SignTransaction, () => _settlement.SettleAsync(GetInt(root, "tradeId") ?? 0, Actor, ct));
            case "refund": return await ActAsync(TradeOperation.Refund, () => _refund.RefundAsync(GetInt(root, "tradeId") ?? 0, Actor, ct));
            case "cancel_trade": return await ActAsync(TradeOperation.CancelTrade, () => _trades.CancelAsync(GetInt(root, "tradeId") ?? 0, Actor, GetString(root, "reason"), ct));
            default: return Json(new { error = $"Unknown tool '{name}'." });
        }
    }

    private async Task<string> ActAsync(TradeOperation op, Func<Task<Trade>> action)
    {
        if (!_user.Can(op))
            return Json(new { error = "permission_denied", operation = op.ToString(), activeRole = _user.ActiveRole?.ToString() });
        var trade = await action();
        return Json(new { ok = true, tradeId = trade.Id, tradeReference = trade.TradeReference, status = trade.Status.ToString() });
    }

    private async Task<string> RunAnalysisAsync(int tradeId, CancellationToken ct)
    {
        if (!_user.Can(TradeOperation.RunAnalysis))
            return Json(new { error = "permission_denied", operation = "RunAnalysis", activeRole = _user.ActiveRole?.ToString() });
        var run = await _verification.RunAnalysisAsync(tradeId, Actor, ct);
        return Json(new { ok = true, tradeId, verdict = run.Result.ToString(), aiRiskLevel = run.AiRiskLevel, summary = run.Summary });
    }

    private async Task<string> CreateTradeAsync(JsonElement root, CancellationToken ct)
    {
        if (!_user.Can(TradeOperation.CreateTrade))
            return Json(new { error = "permission_denied", operation = "CreateTrade", activeRole = _user.ActiveRole?.ToString() });

        var input = new TradeConditionsInput
        {
            BuyerCompanyId = GetInt(root, "buyerCompanyId") ?? 0,
            SellerCompanyId = GetInt(root, "sellerCompanyId") ?? 0,
            BuyerWalletAddress = GetString(root, "buyerWalletAddress") ?? string.Empty,
            SellerWalletAddress = GetString(root, "sellerWalletAddress") ?? string.Empty,
            PaymentAmount = GetDecimal(root, "amount") ?? 0m,
            Currency = GetString(root, "currency") ?? "USDC",
            PaymentToken = GetString(root, "paymentToken") ?? "MockUSDC",
            PurchaseOrderNumber = GetString(root, "purchaseOrderNumber"),
            ExpectedProductDescription = GetString(root, "expectedProductDescription"),
            ExpectedQuantity = GetDecimal(root, "expectedQuantity"),
            LatestShipmentDate = GetDate(root, "latestShipmentDate"),
            Notes = GetString(root, "notes")
        };
        if (Enum.TryParse<TransportMode>(GetString(root, "transportMode"), ignoreCase: true, out var mode))
            input.TransportMode = mode;

        var trade = await _trades.CreateDraftAsync(input, Actor, ct);
        return Json(new { ok = true, tradeId = trade.Id, tradeReference = trade.TradeReference, status = trade.Status.ToString() });
    }

    private async Task<string> ListTradesAsync(string? status, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var query = db.Trades.AsNoTracking().OrderByDescending(t => t.Id).AsQueryable();
        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<TradeStatus>(status, ignoreCase: true, out var s))
            query = query.Where(t => t.Status == s);

        var trades = await query.Take(20).Select(t => new
        {
            id = t.Id,
            tradeReference = t.TradeReference,
            status = t.Status.ToString(),
            amount = t.PaymentAmount,
            currency = t.Currency,
            buyerCompanyId = t.BuyerCompanyId,
            sellerCompanyId = t.SellerCompanyId,
            isFunded = t.IsFunded,
            isSettled = t.IsSettled,
            isRefunded = t.IsRefunded
        }).ToListAsync(ct);

        return Json(new { count = trades.Count, trades });
    }

    private async Task<string> GetTradeAsync(int tradeId, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var t = await db.Trades.AsNoTracking()
            .Include(x => x.BuyerCompany)
            .Include(x => x.SellerCompany)
            .Include(x => x.Documents)
            .FirstOrDefaultAsync(x => x.Id == tradeId, ct);
        if (t is null)
            return Json(new { error = $"Trade {tradeId} not found." });

        return Json(new
        {
            id = t.Id,
            tradeReference = t.TradeReference,
            status = t.Status.ToString(),
            conditionsLocked = t.ConditionsLocked,
            buyer = new { id = t.BuyerCompanyId, name = t.BuyerCompany?.Name },
            seller = new { id = t.SellerCompanyId, name = t.SellerCompany?.Name },
            buyerWalletAddress = t.BuyerWalletAddress,
            sellerWalletAddress = t.SellerWalletAddress,
            transportMode = t.TransportMode.ToString(),
            paymentToken = t.PaymentToken,
            amount = t.PaymentAmount,
            currency = t.Currency,
            expectedQuantity = t.ExpectedQuantity,
            expectedProductDescription = t.ExpectedProductDescription,
            purchaseOrderNumber = t.PurchaseOrderNumber,
            latestShipmentDate = t.LatestShipmentDate,
            paymentExpiry = t.PaymentExpiry,
            latestVerdict = t.LatestVerdict?.ToString(),
            isFunded = t.IsFunded,
            isSettled = t.IsSettled,
            isRefunded = t.IsRefunded,
            documentTypes = t.Documents.Select(d => d.DocumentType.ToString()).Distinct().ToArray()
        });
    }

    private async Task<string> ListCompaniesAsync(CancellationToken ct)
    {
        var companies = await _master.GetCompaniesAsync(ct);
        return Json(new { companies = companies.Select(c => new { id = c.Id, name = c.Name, countryCode = c.CountryCode }) });
    }

    private async Task<string> ListUsersAsync(CancellationToken ct)
    {
        var users = await _master.GetUsersAsync(ct);
        return Json(new
        {
            users = users.Select(u => new
            {
                id = u.Id,
                displayName = u.DisplayName,
                email = u.Email,
                companyId = u.CompanyId,
                roles = u.Roles.Select(r => r.Role.ToString()).ToArray()
            })
        });
    }

    // ---- Tool definitions ------------------------------------------------

    private static IReadOnlyList<ChatTool> BuildTools()
    {
        var tradeIdOnly = """{"type":"object","properties":{"tradeId":{"type":"integer"}},"required":["tradeId"]}""";
        var tradeIdComment = """{"type":"object","properties":{"tradeId":{"type":"integer"},"comment":{"type":"string"}},"required":["tradeId"]}""";

        return new List<ChatTool>
        {
            ChatTool.CreateFunctionTool("list_trades",
                "List up to 20 most recent trades, optionally filtered by status.",
                BinaryData.FromString("""{"type":"object","properties":{"status":{"type":"string","description":"Optional TradeStatus enum name, e.g. Draft, Funded, Settled."}}}""")),
            ChatTool.CreateFunctionTool("get_trade",
                "Get full detail and current state of one trade by id.",
                BinaryData.FromString(tradeIdOnly)),
            ChatTool.CreateFunctionTool("list_companies",
                "List registered companies (for choosing buyer/seller when creating a trade).",
                BinaryData.FromString("""{"type":"object","properties":{}}""")),
            ChatTool.CreateFunctionTool("list_users",
                "List registered users and their roles.",
                BinaryData.FromString("""{"type":"object","properties":{}}""")),
            ChatTool.CreateFunctionTool("create_trade",
                "Create a new draft trade with the given conditions.",
                BinaryData.FromString("""
                {"type":"object","properties":{
                  "buyerCompanyId":{"type":"integer"},
                  "sellerCompanyId":{"type":"integer"},
                  "buyerWalletAddress":{"type":"string"},
                  "sellerWalletAddress":{"type":"string"},
                  "amount":{"type":"number"},
                  "currency":{"type":"string"},
                  "paymentToken":{"type":"string"},
                  "transportMode":{"type":"string","description":"Sea, Air, Land or Rail."},
                  "purchaseOrderNumber":{"type":"string"},
                  "expectedProductDescription":{"type":"string"},
                  "expectedQuantity":{"type":"number"},
                  "latestShipmentDate":{"type":"string","description":"ISO date yyyy-MM-dd."},
                  "notes":{"type":"string"}
                },"required":["buyerCompanyId","sellerCompanyId","buyerWalletAddress","sellerWalletAddress","amount"]}
                """)),
            ChatTool.CreateFunctionTool("request_approval", "Submit a draft trade for buyer-side approval.", BinaryData.FromString(tradeIdOnly)),
            ChatTool.CreateFunctionTool("approve_conditions", "Approve the trade conditions (Buyer Approver).", BinaryData.FromString(tradeIdComment)),
            ChatTool.CreateFunctionTool("fund_escrow", "Fund the escrow for an approved trade (Buyer Approver).", BinaryData.FromString(tradeIdOnly)),
            ChatTool.CreateFunctionTool("run_analysis", "Run document analysis / rule engine for a trade and return the verdict.", BinaryData.FromString(tradeIdOnly)),
            ChatTool.CreateFunctionTool("verify_documents", "Confirm documents as verified (Trade Verifier).", BinaryData.FromString(tradeIdComment)),
            ChatTool.CreateFunctionTool("reject_documents", "Reject the submitted documents with a reason.",
                BinaryData.FromString("""{"type":"object","properties":{"tradeId":{"type":"integer"},"reason":{"type":"string"}},"required":["tradeId","reason"]}""")),
            ChatTool.CreateFunctionTool("raise_manual_review", "Escalate a trade to manual review (Trade Verifier).", BinaryData.FromString(tradeIdComment)),
            ChatTool.CreateFunctionTool("approve_payment", "Approve the payment for a verified trade (Buyer Approver).", BinaryData.FromString(tradeIdComment)),
            ChatTool.CreateFunctionTool("settle", "Sign and settle the payment to the seller (Buyer Approver). Irreversible.", BinaryData.FromString(tradeIdOnly)),
            ChatTool.CreateFunctionTool("refund", "Refund the escrow to the buyer (Buyer Approver). Irreversible.", BinaryData.FromString(tradeIdOnly)),
            ChatTool.CreateFunctionTool("cancel_trade", "Cancel a trade with an optional reason.",
                BinaryData.FromString("""{"type":"object","properties":{"tradeId":{"type":"integer"},"reason":{"type":"string"}},"required":["tradeId"]}""")),
        };
    }

    // ---- JSON helpers ----------------------------------------------------

    private static string Json(object value) => JsonSerializer.Serialize(value, JsonOpts);

    private static string? GetString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String ? el.GetString() : null;

    private static int? GetInt(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var el)) return null;
        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var i)) return i;
        if (el.ValueKind == JsonValueKind.String && int.TryParse(el.GetString(), out var s)) return s;
        return null;
    }

    private static decimal? GetDecimal(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var el)) return null;
        if (el.ValueKind == JsonValueKind.Number && el.TryGetDecimal(out var d)) return d;
        if (el.ValueKind == JsonValueKind.String && decimal.TryParse(el.GetString(), out var s)) return s;
        return null;
    }

    private static DateTime? GetDate(JsonElement root, string name)
    {
        var s = GetString(root, name);
        return DateTime.TryParse(s, out var d) ? d : null;
    }
}

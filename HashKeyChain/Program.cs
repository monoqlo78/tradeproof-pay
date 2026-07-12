using HashKeyChain;
using HashKeyChain.Components;
using HashKeyChain.Data;
using HashKeyChain.Localization;
using Microsoft.AspNetCore.Localization;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Localization: resource files live under Resources/.
builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");

// MVC controllers power the culture switch endpoint and the client-resource JSON
// endpoint. DataAnnotations messages are resolved from the shared resource.
builder.Services.AddControllers()
    .AddDataAnnotationsLocalization(options =>
        options.DataAnnotationLocalizerProvider = (_, factory) =>
            factory.Create(typeof(SharedResource)));

// Localization helper services.
builder.Services.AddScoped<IEnumLocalizer, EnumLocalizer>();
builder.Services.AddScoped<IWeb3MessageLocalizer, Web3MessageLocalizer>();
builder.Services.AddScoped<IClientLocalizationProvider, ClientLocalizationProvider>();
builder.Services.AddScoped<IAgentLanguageContextProvider, AgentLanguageContextProvider>();

// Persistence (provider switch: None / SqlServer / InMemory). All objects live
// in the dedicated hashkeychain schema; existing data is untouched.
builder.Services.AddHashKeyChainPersistence(builder.Configuration);

// Blockchain configuration (chain doc §3). Values come from config, validated at
// startup; never hardcoded. DemoMode uses an in-process mock escrow.
builder.Services.AddOptions<HashKeyChain.Configuration.BlockchainOptions>()
    .Bind(builder.Configuration.GetSection(HashKeyChain.Configuration.BlockchainOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<Microsoft.Extensions.Options.IValidateOptions<HashKeyChain.Configuration.BlockchainOptions>,
    HashKeyChain.Configuration.BlockchainOptionsValidator>();
// DemoMode uses the in-process mock; Testnet/Mainnet talk to real HashKey Chain
// contracts via Nethereum (chain doc §3).
var blockchainEnv = builder.Configuration
    .GetValue<HashKeyChain.Configuration.BlockchainEnvironment>(
        $"{HashKeyChain.Configuration.BlockchainOptions.SectionName}:Environment");
if (blockchainEnv == HashKeyChain.Configuration.BlockchainEnvironment.DemoMode)
{
    builder.Services.AddSingleton<HashKeyChain.Services.Blockchain.IEscrowChainService,
        HashKeyChain.Services.Blockchain.MockEscrowChainService>();
}
else
{
    builder.Services.AddSingleton<HashKeyChain.Services.Blockchain.IEscrowChainService,
        HashKeyChain.Services.Blockchain.HashKeyEscrowChainService>();
}

// Trade lifecycle + role context.
builder.Services.AddSingleton<HashKeyChain.Services.Trades.ITradeStateMachine,
    HashKeyChain.Services.Trades.TradeStateMachine>();
builder.Services.AddScoped<HashKeyChain.Services.Security.ICurrentUserContext,
    HashKeyChain.Services.Security.CurrentUserContext>();
builder.Services.AddScoped<HashKeyChain.Services.Trades.IAuditWriter,
    HashKeyChain.Services.Trades.AuditWriter>();
builder.Services.AddScoped<HashKeyChain.Services.Trades.ITradeService,
    HashKeyChain.Services.Trades.TradeService>();
builder.Services.AddScoped<HashKeyChain.Services.Trades.IEscrowService,
    HashKeyChain.Services.Trades.EscrowService>();

// Document submission (versioning + SHA-256 + private storage).
builder.Services.Configure<HashKeyChain.Services.Documents.DocumentStorageOptions>(
    builder.Configuration.GetSection(HashKeyChain.Services.Documents.DocumentStorageOptions.SectionName));
builder.Services.AddSingleton<HashKeyChain.Services.Documents.IDocumentStorage,
    HashKeyChain.Services.Documents.LocalFileDocumentStorage>();
builder.Services.AddScoped<HashKeyChain.Services.Documents.IDocumentService,
    HashKeyChain.Services.Documents.DocumentService>();

// Document analysis: real Azure Document Intelligence + LLM normalization when
// configured, otherwise the deterministic mock. The mock is always registered so
// it can serve as the demo-JSON path and the safe fallback. Rule engine unchanged.
builder.Services.AddOptions<HashKeyChain.Configuration.AgentOptions>()
    .Bind(builder.Configuration.GetSection(HashKeyChain.Configuration.AgentOptions.SectionName));
builder.Services.AddOptions<HashKeyChain.Configuration.DocumentIntelligenceOptions>()
    .Bind(builder.Configuration.GetSection(HashKeyChain.Configuration.DocumentIntelligenceOptions.SectionName));
builder.Services.AddOptions<HashKeyChain.Configuration.ContentUnderstandingOptions>()
    .Bind(builder.Configuration.GetSection(HashKeyChain.Configuration.ContentUnderstandingOptions.SectionName));
builder.Services.AddSingleton<HashKeyChain.Services.Agent.IChatClientProvider,
    HashKeyChain.Services.Agent.ChatClientProvider>();
builder.Services.AddScoped<HashKeyChain.Services.Agent.ITradeAgentService,
    HashKeyChain.Services.Agent.TradeAgentService>();

// Azure AI Content Understanding client + contract auto-fill (used to pre-fill the
// trade creation form from an uploaded contract). Degrades to unavailable when not
// configured, so the UI simply hides the feature.
builder.Services.AddSingleton<HashKeyChain.Services.Analysis.ContentUnderstandingClient>();
builder.Services.AddSingleton<HashKeyChain.Services.Analysis.IContractExtractionService,
    HashKeyChain.Services.Analysis.ContentUnderstandingContractExtractionService>();

builder.Services.AddSingleton<HashKeyChain.Services.Analysis.MockDocumentAnalysisService>();
builder.Services.AddSingleton<HashKeyChain.Services.Analysis.IDocumentAnalysisService>(sp =>
{
    // Priority: Content Understanding (real, generative field extraction) >
    // Document Intelligence + LLM > deterministic mock. Any of them falls back to
    // the mock internally, so the rule engine and DemoMode are unaffected.
    var cu = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<
        HashKeyChain.Configuration.ContentUnderstandingOptions>>().Value;
    if (cu.IsConfigured)
        return ActivatorUtilities.CreateInstance<HashKeyChain.Services.Analysis.ContentUnderstandingDocumentAnalysisService>(sp);

    var di = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<
        HashKeyChain.Configuration.DocumentIntelligenceOptions>>().Value;
    var chat = sp.GetRequiredService<HashKeyChain.Services.Agent.IChatClientProvider>();
    if (di.IsConfigured && chat.IsAvailable)
        return ActivatorUtilities.CreateInstance<HashKeyChain.Services.Analysis.AzureDocumentIntelligenceAnalysisService>(sp);
    return sp.GetRequiredService<HashKeyChain.Services.Analysis.MockDocumentAnalysisService>();
});
builder.Services.AddSingleton<HashKeyChain.Services.Analysis.ITradeRuleEngine,
    HashKeyChain.Services.Analysis.TradeRuleEngine>();
builder.Services.AddScoped<HashKeyChain.Services.Verification.IVerificationService,
    HashKeyChain.Services.Verification.VerificationService>();

// Settlement + refund (receipt-confirmed-before-final; double-pay/refund guards).
builder.Services.AddScoped<HashKeyChain.Services.Settlement.ISettlementService,
    HashKeyChain.Services.Settlement.SettlementService>();
builder.Services.AddScoped<HashKeyChain.Services.Settlement.IRefundService,
    HashKeyChain.Services.Settlement.RefundService>();

// Demo data seeding (companies + one user per role) for DemoMode.
builder.Services.AddScoped<HashKeyChain.Services.Master.IMasterDataService,
    HashKeyChain.Services.Master.MasterDataService>();
builder.Services.AddSingleton<HashKeyChain.Services.Settings.IOrgSettingsService,
    HashKeyChain.Services.Settings.OrgSettingsService>();
builder.Services.AddScoped<HashKeyChain.Services.Demo.IDemoDataSeeder,
    HashKeyChain.Services.Demo.DemoDataSeeder>();

// Demo scenario runner (end-to-end lifecycle driver; --run-demo-flow).
builder.Services.AddScoped<HashKeyChain.Services.Demo.IDemoScenarioRunner,
    HashKeyChain.Services.Demo.DemoScenarioRunner>();

// Supported cultures: Japanese (default) and English.
builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    var cultures = LocalizationConstants.SupportedCultureInfos;
    options.DefaultRequestCulture = new RequestCulture(LocalizationConstants.DefaultCulture);
    options.SupportedCultures = cultures;
    options.SupportedUICultures = cultures;
    options.ApplyCurrentCultureToResponseHeaders = true;

    // Priority: 1) signed-in user's PreferredCulture, 2) culture cookie,
    // 3) Accept-Language header, 4) default (ja-JP).
    options.RequestCultureProviders = new List<IRequestCultureProvider>
    {
        new UserProfileRequestCultureProvider(),
        new CookieRequestCultureProvider(),
        new AcceptLanguageHeaderRequestCultureProvider()
    };
});

var app = builder.Build();

// Ensure the hashkeychain schema exists when explicitly enabled (admin/DDL only,
// additive). Skipped for the runtime managed identity and when no DB configured.
await app.Services.ApplyDatabaseBootstrapAsync(
    app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Database"));

// Seed demo companies/users against a real database, independent of the chain
// environment (the demo data is required to create trades on Testnet too).
{
    var dbOptions = app.Services.GetRequiredService<
        Microsoft.Extensions.Options.IOptions<DatabaseOptions>>().Value;
    if (dbOptions.Provider != DatabaseProvider.None)
    {
        try
        {
            using var scope = app.Services.CreateScope();
            await scope.ServiceProvider
                .GetRequiredService<HashKeyChain.Services.Demo.IDemoDataSeeder>()
                .SeedAsync();
        }
        catch (Exception ex)
        {
            app.Services.GetRequiredService<ILoggerFactory>()
                .CreateLogger("DemoSeed").LogError(ex, "Demo data seeding failed; continuing.");
        }
    }
}

// Optional: run full end-to-end demo scenarios then exit (verification / demo
// data population). Triggered by "--run-demo-flow". Reuses all app services and
// the configured database; writes stay in the hashkeychain schema.
if (args.Contains("--run-demo-flow"))
{
    using var scope = app.Services.CreateScope();
    var runner = scope.ServiceProvider.GetRequiredService<HashKeyChain.Services.Demo.IDemoScenarioRunner>();
    var outcomes = await runner.RunAsync();
    Console.WriteLine("=== Demo scenario results ===");
    foreach (var line in outcomes)
        Console.WriteLine(line);
    return;
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

// Apply the request culture as early as possible so components and controllers
// observe the correct CurrentCulture / CurrentUICulture.
app.UseRequestLocalization(app.Services.GetRequiredService<
    Microsoft.Extensions.Options.IOptions<RequestLocalizationOptions>>().Value);

app.UseAntiforgery();

app.MapStaticAssets();
app.MapControllers();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

// Exposed for integration tests (WebApplicationFactory<Program>).
public partial class Program;

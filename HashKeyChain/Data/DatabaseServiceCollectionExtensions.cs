using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HashKeyChain.Data;

/// <summary>
/// Registers persistence services with a provider switch. Designed so the app
/// runs everywhere: with no database (None → in-memory null store), against Azure
/// SQL (SqlServer, Entra auth) locally and in the cloud, or InMemory for tests.
/// All schema objects live in the dedicated <c>hashkeychain</c> schema and the
/// bootstrap is additive-only — existing tables and data are never touched.
/// </summary>
public static class DatabaseServiceCollectionExtensions
{
    public static IServiceCollection AddHashKeyChainPersistence(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<DatabaseOptions>(configuration.GetSection(DatabaseOptions.SectionName));

        var options = configuration.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>()
                      ?? new DatabaseOptions();

        switch (options.Provider)
        {
            case DatabaseProvider.SqlServer:
                services.AddDbContextFactory<AppDbContext>(db =>
                    db.UseSqlServer(options.ConnectionString, sql =>
                        sql.MigrationsHistoryTable("__EFMigrationsHistory", AppDbContext.Schema)));
                services.AddScoped<ITradeRequestStore, SqlTradeRequestStore>();
                break;

            case DatabaseProvider.InMemory:
                services.AddDbContextFactory<AppDbContext>(db =>
                    db.UseInMemoryDatabase(options.ConnectionString ?? "hashkeychain-tests"));
                services.AddScoped<ITradeRequestStore, SqlTradeRequestStore>();
                break;

            default:
                services.AddScoped<ITradeRequestStore, NullTradeRequestStore>();
                break;
        }

        return services;
    }

    /// <summary>
    /// Ensures the hashkeychain schema/tables exist when explicitly enabled via
    /// <see cref="DatabaseOptions.ApplyBootstrap"/>. This MUST only run under an
    /// admin identity with DDL rights (never the app's runtime managed identity).
    /// It is additive-only (EF migrations / EnsureCreated for InMemory) and never
    /// drops or alters objects outside the hashkeychain schema.
    /// </summary>
    public static async Task ApplyDatabaseBootstrapAsync(this IServiceProvider services, ILogger logger)
    {
        var options = services.GetRequiredService<IOptions<DatabaseOptions>>().Value;

        if (options.Provider is DatabaseProvider.None)
            return;

        try
        {
            var factory = services.GetRequiredService<IDbContextFactory<AppDbContext>>();
            await using var db = await factory.CreateDbContextAsync();

            if (options.Provider is DatabaseProvider.InMemory)
            {
                await db.Database.EnsureCreatedAsync();
                return;
            }

            if (!options.ApplyBootstrap)
            {
                var canConnect = await db.Database.CanConnectAsync();
                logger.LogInformation(
                    "Database bootstrap skipped (ApplyBootstrap=false). Connectivity check: {CanConnect}. " +
                    "Assuming hashkeychain schema already provisioned.", canConnect);
                return;
            }

            logger.LogInformation("Applying additive hashkeychain-schema migrations…");
            await db.Database.MigrateAsync();
            logger.LogInformation("hashkeychain schema is up to date.");
        }
        catch (Exception ex)
        {
            // Never crash the app on DB issues — the app remains usable and the
            // problem is surfaced in logs.
            logger.LogError(ex, "Database bootstrap failed; continuing without guaranteed schema.");
        }
    }
}

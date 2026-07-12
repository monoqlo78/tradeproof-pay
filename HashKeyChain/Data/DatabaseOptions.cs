namespace HashKeyChain.Data;

/// <summary>
/// Supported EF Core providers. DemoMode/local and Azure use SqlServer against
/// the dedicated <c>hashkeychain</c> schema; tests use InMemory; None disables
/// persistence entirely.
/// </summary>
public enum DatabaseProvider
{
    None,
    SqlServer,
    InMemory
}

/// <summary>
/// Strongly-typed database configuration (bound from the <c>Database</c> section).
/// </summary>
public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>Which EF Core provider to use.</summary>
    public DatabaseProvider Provider { get; set; } = DatabaseProvider.None;

    /// <summary>
    /// The connection string. For Azure SQL (Entra-only auth) this uses
    /// <c>Authentication=Active Directory Default</c> locally (az CLI login) or
    /// <c>Active Directory Managed Identity</c> on App Service.
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// When true, the schema/tables are ensured at startup using an idempotent,
    /// additive bootstrap (hashkeychain schema only). MUST be false for the app's
    /// runtime managed identity (which has no DDL rights). Only an admin identity
    /// with DDL rights should run the bootstrap. Never destructive.
    /// </summary>
    public bool ApplyBootstrap { get; set; }
}

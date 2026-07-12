using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HashKeyChain.Data;

/// <summary>
/// Design-time factory used only by <c>dotnet ef</c> to generate/apply
/// migrations. It configures the SqlServer provider with the hashkeychain
/// migrations-history table. The connection string here is a placeholder — the
/// runtime connection comes from configuration. Migration <em>generation</em>
/// never connects to a database.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("HASHKEYCHAIN_DESIGN_CONNECTION")
            ?? "Server=localhost;Database=design_time;Trusted_Connection=True;TrustServerCertificate=True;";

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString, sql =>
                sql.MigrationsHistoryTable("__EFMigrationsHistory", AppDbContext.Schema))
            .Options;

        return new AppDbContext(options);
    }
}

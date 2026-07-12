using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace HashKeyChain.Tests;

/// <summary>
/// WebApplicationFactory that forces the in-memory database and DemoMode chain so
/// integration tests never reach Azure SQL or a real RPC endpoint. Each factory
/// instance gets an isolated in-memory database name.
/// </summary>
public sealed class TestAppFactory : WebApplicationFactory<Program>
{
    private readonly string _dbName = $"hashkeychain-tests-{Guid.NewGuid():N}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Provider"] = "InMemory",
                ["Database:ConnectionString"] = _dbName,
                ["Database:ApplyBootstrap"] = "false",
                ["Blockchain:Environment"] = "DemoMode"
            });
        });
    }
}

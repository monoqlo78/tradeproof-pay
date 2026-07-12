using HashKeyChain.Models;
using Microsoft.EntityFrameworkCore;

namespace HashKeyChain.Data;

/// <summary>
/// SQL-backed store using a DbContextFactory (recommended for Blazor Server so
/// each operation gets its own short-lived context).
/// </summary>
public sealed class SqlTradeRequestStore : ITradeRequestStore
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public SqlTradeRequestStore(IDbContextFactory<AppDbContext> contextFactory) =>
        _contextFactory = contextFactory;

    public bool IsEnabled => true;

    public async Task SaveAsync(TradeRequestModel model, string uiCulture, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        context.TradeRequests.Add(new TradeRequestRecord
        {
            TradeReference = model.TradeReference ?? string.Empty,
            Amount = model.Amount,
            Email = model.Email ?? string.Empty,
            UiCulture = uiCulture,
            CreatedAtUtc = DateTime.UtcNow
        });
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.TradeRequests.CountAsync(cancellationToken);
    }
}

/// <summary>
/// No-op store used when no database connection string is configured.
/// </summary>
public sealed class NullTradeRequestStore : ITradeRequestStore
{
    public bool IsEnabled => false;

    public Task SaveAsync(TradeRequestModel model, string uiCulture, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task<int> CountAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
}

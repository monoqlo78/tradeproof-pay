using HashKeyChain.Models;

namespace HashKeyChain.Data;

/// <summary>
/// Persists trade-demo submissions. Abstracted so the app runs unchanged when no
/// database is configured (e.g. local development).
/// </summary>
public interface ITradeRequestStore
{
    bool IsEnabled { get; }

    Task SaveAsync(TradeRequestModel model, string uiCulture, CancellationToken cancellationToken = default);

    Task<int> CountAsync(CancellationToken cancellationToken = default);
}

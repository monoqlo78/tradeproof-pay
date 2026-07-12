using HashKeyChain.Domain;

namespace HashKeyChain.Services.Security;

/// <summary>
/// The currently acting demo user and role. In the MVP there is no production
/// identity provider; a user is selected from seeded demo users and the active
/// role is shown in the UI at all times (spec §4). Scoped to the Blazor circuit.
/// </summary>
public interface ICurrentUserContext
{
    AppUser? User { get; }
    TradeRole? ActiveRole { get; }
    IReadOnlyCollection<TradeRole> HeldRoles { get; }

    bool IsAuthenticated { get; }
    bool Can(TradeOperation operation);

    void SetUser(AppUser user, TradeRole? activeRole = null);
    void SetActiveRole(TradeRole role);
    void Clear();

    event Action? Changed;
}

public sealed class CurrentUserContext : ICurrentUserContext
{
    public AppUser? User { get; private set; }
    public TradeRole? ActiveRole { get; private set; }

    public IReadOnlyCollection<TradeRole> HeldRoles =>
        User?.Roles.Select(r => r.Role).Distinct().ToArray() ?? Array.Empty<TradeRole>();

    public bool IsAuthenticated => User is not null;

    public event Action? Changed;

    public bool Can(TradeOperation operation)
    {
        if (User is null)
            return false;
        // The active role governs what the user may do right now (separation of
        // duties), but the user must actually hold that role.
        if (ActiveRole is { } role && HeldRoles.Contains(role))
            return TradePermissions.IsAllowed(operation, role);
        return TradePermissions.IsAllowed(operation, HeldRoles);
    }

    public void SetUser(AppUser user, TradeRole? activeRole = null)
    {
        User = user ?? throw new ArgumentNullException(nameof(user));
        var held = HeldRoles;
        ActiveRole = activeRole is { } r && held.Contains(r) ? r : held.FirstOrDefault();
        Changed?.Invoke();
    }

    public void SetActiveRole(TradeRole role)
    {
        if (!HeldRoles.Contains(role))
            throw new InvalidOperationException($"Current user does not hold role {role}.");
        ActiveRole = role;
        Changed?.Invoke();
    }

    public void Clear()
    {
        User = null;
        ActiveRole = null;
        Changed?.Invoke();
    }
}

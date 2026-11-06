namespace MestoBeach.Shared.Context;

/// <summary>
/// Holds the resolved tenant for the current request.
/// Populated by TenantMiddleware from the JWT or session token.
/// All repositories automatically filter by this ID.
/// </summary>
public interface ITenantContext
{
    Guid TenantId { get; }
    bool IsResolved { get; }
}

public sealed class TenantContext : ITenantContext
{
    private Guid _tenantId;
    private bool _isResolved;

    public Guid TenantId => _isResolved
        ? _tenantId
        : throw new InvalidOperationException("TenantContext has not been resolved for this request.");

    public bool IsResolved => _isResolved;

    public void Set(Guid tenantId)
    {
        _tenantId = tenantId;
        _isResolved = true;
    }
}

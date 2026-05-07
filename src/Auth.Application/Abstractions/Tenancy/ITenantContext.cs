namespace Auth.Application.Abstractions.Tenancy;

public interface ITenantContext
{
    /// <summary>Tenant id from the current authenticated principal. Throws if no tenant is in scope.</summary>
    Guid TenantId { get; }

    /// <summary>True when there is a tenant in scope (e.g. inside a request); false during startup or background work.</summary>
    bool HasTenant { get; }
}

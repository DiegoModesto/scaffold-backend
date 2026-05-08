using Auth.Application.Abstractions.Tenancy;

namespace Auth.Application.UnitTests.Infrastructure;

public sealed class StubTenantContext(Guid tenantId) : ITenantContext
{
    public Guid TenantId { get; } = tenantId;
    public bool HasTenant => true;
}

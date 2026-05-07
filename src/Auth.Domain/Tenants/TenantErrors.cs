using SharedKernel;

namespace Auth.Domain.Tenants;

public static class TenantErrors
{
    public static readonly Error Inactive = Error.Forbidden(
        "Tenant.Inactive",
        "The tenant is inactive and cannot be used.");

    public static Error NotRegistered(Guid entraTenantId) => Error.NotFound(
        "Tenant.NotRegistered",
        $"The tenant with Entra tenant id '{entraTenantId}' is not registered.");
}

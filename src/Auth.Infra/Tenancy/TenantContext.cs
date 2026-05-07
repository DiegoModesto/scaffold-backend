using System.Globalization;
using System.Security.Claims;
using Auth.Application.Abstractions.Tenancy;
using Microsoft.AspNetCore.Http;

namespace Auth.Infra.Tenancy;

internal sealed class TenantContext(IHttpContextAccessor httpContextAccessor) : ITenantContext
{
    private const string TenantClaim = "tenant_id";

    public bool HasTenant => TryGetTenantId(out _);

    public Guid TenantId =>
        TryGetTenantId(out Guid tenantId)
            ? tenantId
            : throw new InvalidOperationException(
                "No tenant is in scope. Check ITenantContext.HasTenant before reading TenantId.");

    private bool TryGetTenantId(out Guid tenantId)
    {
        tenantId = Guid.Empty;

        HttpContext? ctx = httpContextAccessor.HttpContext;
        if (ctx is null)
        {
            return false;
        }

        Claim? claim = ctx.User?.FindFirst(TenantClaim);
        if (claim is null)
        {
            return false;
        }

        return Guid.TryParse(claim.Value, CultureInfo.InvariantCulture, out tenantId);
    }
}

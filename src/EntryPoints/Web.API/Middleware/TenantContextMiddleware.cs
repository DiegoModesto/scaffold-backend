using System.Diagnostics;
using System.Security.Claims;

namespace Web.API.Middleware;

internal sealed class TenantContextMiddleware(
    RequestDelegate next,
    ILogger<TenantContextMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        string? claim = context.User.FindFirstValue("tenant_id");
        string? header = context.Request.Headers["X-Forwarded-TenantId"].FirstOrDefault();

        if (!string.IsNullOrEmpty(claim) && !string.IsNullOrEmpty(header) && claim != header)
        {
            logger.LogWarning(
                "Tenant id mismatch: claim={Claim} header={Header}. Trusting claim.",
                claim, header);
        }

        string? tenantId = string.IsNullOrEmpty(claim) ? header : claim;
        if (!string.IsNullOrEmpty(tenantId))
        {
            Activity.Current?.SetTag("tenant.id", tenantId);
        }

        await next(context);
    }
}

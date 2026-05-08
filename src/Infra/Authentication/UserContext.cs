using System.Security.Claims;
using Application.Abstractions.Authentication;
using Microsoft.AspNetCore.Http;

namespace Infra.Authentication;

internal sealed class UserContext(IHttpContextAccessor httpContextAccessor) : IUserContext
{
    public Guid UserId =>
        httpContextAccessor
            .HttpContext?
            .User
            .FindFirstValue(ClaimTypes.NameIdentifier) is { } userId
            ? Guid.Parse(userId)
            : throw new InvalidClaimException("User id is unavailable");

    public Guid? TenantId
    {
        get
        {
            HttpContext? httpContext = httpContextAccessor.HttpContext;
            if (httpContext is null)
            {
                return null;
            }

            string? claim = httpContext.User.FindFirstValue("tenant_id");
            if (Guid.TryParse(claim, out Guid fromClaim))
            {
                return fromClaim;
            }

            string? header = httpContext.Request.Headers["X-Forwarded-TenantId"].FirstOrDefault();
            return Guid.TryParse(header, out Guid fromHeader) ? fromHeader : null;
        }
    }

    public bool IsAuthenticated =>
        httpContextAccessor.HttpContext?.User.Identity?.IsAuthenticated ?? false;
}

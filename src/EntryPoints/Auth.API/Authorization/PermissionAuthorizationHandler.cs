using Microsoft.AspNetCore.Authorization;

namespace Auth.API.Authorization;

internal sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        var has = context.User.Claims.Any(c =>
            c.Type == "permission" && c.Value == requirement.Permission);
        if (has)
        {
            context.Succeed(requirement);
        }
        return Task.CompletedTask;
    }
}

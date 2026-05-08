using Infra.Authorization;
using Auth.API.Extensions;
using Auth.API.Infrastructure;
using Auth.Application.Abstractions.Messaging;
using Auth.Application.Admin.Permissions.ListPermissions;
using Auth.Domain.Permissions;
using SharedKernel;

namespace Auth.API.Endpoints.Admin;

internal sealed class PermissionsEndpoints : IEndpoint
{
    // PermissionCodes does not currently include a "permissions.read" entry. Listing the
    // catalog of permissions is primarily used during role creation (so an admin can pick
    // permissions to attach to a role), so we gate it behind roles.read.
    private const string PolicyRead = $"{PermissionPolicyProvider.PolicyPrefix}{PermissionCodes.RolesRead}";

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/admin/permissions").WithTags("Admin: Permissions");

        group.MapGet("/", async (
            IQueryHandler<ListPermissionsQuery, IReadOnlyCollection<PermissionResponse>> handler,
            CancellationToken ct) =>
        {
            Result<IReadOnlyCollection<PermissionResponse>> result =
                await handler.Handle(new ListPermissionsQuery(), ct);
            return result.Match(Results.Ok, CustomResults.Problem);
        }).RequireAuthorization(PolicyRead);
    }
}

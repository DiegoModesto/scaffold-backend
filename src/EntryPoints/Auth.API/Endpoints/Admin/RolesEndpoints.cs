using Infra.Authorization;
using Auth.API.Extensions;
using Auth.API.Infrastructure;
using Auth.Application.Abstractions.Messaging;
using Auth.Application.Admin.Roles.AssignPermission;
using Auth.Application.Admin.Roles.Create;
using Auth.Application.Admin.Roles.Delete;
using Auth.Application.Admin.Roles.GetRole;
using Auth.Application.Admin.Roles.ListRoles;
using Auth.Application.Admin.Roles.RevokePermission;
using Auth.Application.Admin.Roles.Update;
using Auth.Application.Common;
using Auth.Domain.Permissions;
using SharedKernel;

namespace Auth.API.Endpoints.Admin;

internal sealed class RolesEndpoints : IEndpoint
{
    private const string PolicyRead = $"{PermissionPolicyProvider.PolicyPrefix}{PermissionCodes.RolesRead}";
    private const string PolicyWrite = $"{PermissionPolicyProvider.PolicyPrefix}{PermissionCodes.RolesWrite}";

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/admin/roles").WithTags("Admin: Roles");

        group.MapGet("/", async (
            int? page,
            int? pageSize,
            string? search,
            IQueryHandler<ListRolesQuery, PagedResponse<RoleSummary>> handler,
            CancellationToken ct) =>
        {
            Result<PagedResponse<RoleSummary>> result = await handler.Handle(
                new ListRolesQuery(page ?? 1, pageSize ?? 20, search), ct);
            return result.Match(Results.Ok, CustomResults.Problem);
        }).RequireAuthorization(PolicyRead);

        group.MapGet("/{id:guid}", async (
            Guid id,
            IQueryHandler<GetRoleByIdQuery, RoleDetailResponse> handler,
            CancellationToken ct) =>
        {
            Result<RoleDetailResponse> result = await handler.Handle(new GetRoleByIdQuery(id), ct);
            return result.Match(Results.Ok, CustomResults.Problem);
        }).RequireAuthorization(PolicyRead);

        group.MapPost("/", async (
            CreateRoleCommand cmd,
            ICommandHandler<CreateRoleCommand, Guid> handler,
            CancellationToken ct) =>
        {
            Result<Guid> result = await handler.Handle(cmd, ct);
            return result.Match(
                id => Results.Created($"/admin/roles/{id}", new { id }),
                CustomResults.Problem);
        }).RequireAuthorization(PolicyWrite);

        group.MapPut("/{id:guid}", async (
            Guid id,
            UpdateRoleRequest req,
            ICommandHandler<UpdateRoleCommand> handler,
            CancellationToken ct) =>
        {
            Result result = await handler.Handle(
                new UpdateRoleCommand(id, req.Name, req.Description), ct);
            return result.Match(Results.NoContent, CustomResults.Problem);
        }).RequireAuthorization(PolicyWrite);

        group.MapDelete("/{id:guid}", async (
            Guid id,
            ICommandHandler<DeleteRoleCommand> handler,
            CancellationToken ct) =>
        {
            Result result = await handler.Handle(new DeleteRoleCommand(id), ct);
            return result.Match(Results.NoContent, CustomResults.Problem);
        }).RequireAuthorization(PolicyWrite);

        group.MapPost("/{roleId:guid}/permissions/{permissionId:guid}", async (
            Guid roleId,
            Guid permissionId,
            ICommandHandler<AssignPermissionToRoleCommand> handler,
            CancellationToken ct) =>
        {
            Result result = await handler.Handle(
                new AssignPermissionToRoleCommand(roleId, permissionId), ct);
            return result.Match(Results.NoContent, CustomResults.Problem);
        }).RequireAuthorization(PolicyWrite);

        group.MapDelete("/{roleId:guid}/permissions/{permissionId:guid}", async (
            Guid roleId,
            Guid permissionId,
            ICommandHandler<RevokePermissionFromRoleCommand> handler,
            CancellationToken ct) =>
        {
            Result result = await handler.Handle(
                new RevokePermissionFromRoleCommand(roleId, permissionId), ct);
            return result.Match(Results.NoContent, CustomResults.Problem);
        }).RequireAuthorization(PolicyWrite);
    }

    private sealed record UpdateRoleRequest(string Name, string Description);
}

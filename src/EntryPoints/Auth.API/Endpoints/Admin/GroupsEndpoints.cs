using Auth.API.Authorization;
using Auth.API.Extensions;
using Auth.API.Infrastructure;
using Auth.Application.Abstractions.Messaging;
using Auth.Application.Admin.Groups.AssignRole;
using Auth.Application.Admin.Groups.Create;
using Auth.Application.Admin.Groups.Delete;
using Auth.Application.Admin.Groups.GetGroup;
using Auth.Application.Admin.Groups.ListGroups;
using Auth.Application.Admin.Groups.RevokeRole;
using Auth.Application.Admin.Groups.Update;
using Auth.Application.Common;
using Auth.Domain.Permissions;
using SharedKernel;

namespace Auth.API.Endpoints.Admin;

internal sealed class GroupsEndpoints : IEndpoint
{
    private const string PolicyRead = $"{PermissionPolicyProvider.PolicyPrefix}{PermissionCodes.GroupsRead}";
    private const string PolicyWrite = $"{PermissionPolicyProvider.PolicyPrefix}{PermissionCodes.GroupsWrite}";

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/admin/groups").WithTags("Admin: Groups");

        group.MapGet("/", async (
            int? page,
            int? pageSize,
            string? search,
            IQueryHandler<ListGroupsQuery, PagedResponse<GroupSummary>> handler,
            CancellationToken ct) =>
        {
            Result<PagedResponse<GroupSummary>> result = await handler.Handle(
                new ListGroupsQuery(page ?? 1, pageSize ?? 20, search), ct);
            return result.Match(Results.Ok, CustomResults.Problem);
        }).RequireAuthorization(PolicyRead);

        group.MapGet("/{id:guid}", async (
            Guid id,
            IQueryHandler<GetGroupByIdQuery, GroupDetailResponse> handler,
            CancellationToken ct) =>
        {
            Result<GroupDetailResponse> result = await handler.Handle(new GetGroupByIdQuery(id), ct);
            return result.Match(Results.Ok, CustomResults.Problem);
        }).RequireAuthorization(PolicyRead);

        group.MapPost("/", async (
            CreateGroupCommand cmd,
            ICommandHandler<CreateGroupCommand, Guid> handler,
            CancellationToken ct) =>
        {
            Result<Guid> result = await handler.Handle(cmd, ct);
            return result.Match(
                id => Results.Created($"/admin/groups/{id}", new { id }),
                CustomResults.Problem);
        }).RequireAuthorization(PolicyWrite);

        group.MapPut("/{id:guid}", async (
            Guid id,
            UpdateGroupRequest req,
            ICommandHandler<UpdateGroupCommand> handler,
            CancellationToken ct) =>
        {
            Result result = await handler.Handle(
                new UpdateGroupCommand(id, req.Name, req.Description, req.EntraGroupId), ct);
            return result.Match(Results.NoContent, CustomResults.Problem);
        }).RequireAuthorization(PolicyWrite);

        group.MapDelete("/{id:guid}", async (
            Guid id,
            ICommandHandler<DeleteGroupCommand> handler,
            CancellationToken ct) =>
        {
            Result result = await handler.Handle(new DeleteGroupCommand(id), ct);
            return result.Match(Results.NoContent, CustomResults.Problem);
        }).RequireAuthorization(PolicyWrite);

        group.MapPost("/{groupId:guid}/roles/{roleId:guid}", async (
            Guid groupId,
            Guid roleId,
            ICommandHandler<AssignRoleToGroupCommand> handler,
            CancellationToken ct) =>
        {
            Result result = await handler.Handle(new AssignRoleToGroupCommand(groupId, roleId), ct);
            return result.Match(Results.NoContent, CustomResults.Problem);
        }).RequireAuthorization(PolicyWrite);

        group.MapDelete("/{groupId:guid}/roles/{roleId:guid}", async (
            Guid groupId,
            Guid roleId,
            ICommandHandler<RevokeRoleFromGroupCommand> handler,
            CancellationToken ct) =>
        {
            Result result = await handler.Handle(new RevokeRoleFromGroupCommand(groupId, roleId), ct);
            return result.Match(Results.NoContent, CustomResults.Problem);
        }).RequireAuthorization(PolicyWrite);
    }

    private sealed record UpdateGroupRequest(string Name, string Description, Guid? EntraGroupId);
}

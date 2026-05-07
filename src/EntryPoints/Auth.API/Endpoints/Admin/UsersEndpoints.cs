using Auth.API.Authorization;
using Auth.API.Extensions;
using Auth.API.Infrastructure;
using Auth.Application.Abstractions.Messaging;
using Auth.Application.Admin.Users.AddToGroup;
using Auth.Application.Admin.Users.AssignRole;
using Auth.Application.Admin.Users.Disable;
using Auth.Application.Admin.Users.Enable;
using Auth.Application.Admin.Users.GetUser;
using Auth.Application.Admin.Users.ListUsers;
using Auth.Application.Admin.Users.PreProvision;
using Auth.Application.Admin.Users.RemoveFromGroup;
using Auth.Application.Admin.Users.RevokeRole;
using Auth.Application.Admin.Users.SetNetSuiteEmail;
using Auth.Application.Common;
using Auth.Domain.Permissions;
using SharedKernel;

namespace Auth.API.Endpoints.Admin;

internal sealed class UsersEndpoints : IEndpoint
{
    private const string PolicyRead = $"{PermissionPolicyProvider.PolicyPrefix}{PermissionCodes.UsersRead}";
    private const string PolicyWrite = $"{PermissionPolicyProvider.PolicyPrefix}{PermissionCodes.UsersWrite}";

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/admin/users").WithTags("Admin: Users");

        group.MapGet("/", async (
            int? page,
            int? pageSize,
            string? search,
            IQueryHandler<ListUsersQuery, PagedResponse<UserSummary>> handler,
            CancellationToken ct) =>
        {
            Result<PagedResponse<UserSummary>> result = await handler.Handle(
                new ListUsersQuery(page ?? 1, pageSize ?? 20, search), ct);
            return result.Match(Results.Ok, CustomResults.Problem);
        }).RequireAuthorization(PolicyRead);

        group.MapGet("/{id:guid}", async (
            Guid id,
            IQueryHandler<GetUserByIdQuery, UserDetailResponse> handler,
            CancellationToken ct) =>
        {
            Result<UserDetailResponse> result = await handler.Handle(new GetUserByIdQuery(id), ct);
            return result.Match(Results.Ok, CustomResults.Problem);
        }).RequireAuthorization(PolicyRead);

        group.MapPost("/pre-provision", async (
            PreProvisionUserCommand cmd,
            ICommandHandler<PreProvisionUserCommand, Guid> handler,
            CancellationToken ct) =>
        {
            Result<Guid> result = await handler.Handle(cmd, ct);
            return result.Match(
                id => Results.Created($"/admin/users/{id}", new { id }),
                CustomResults.Problem);
        }).RequireAuthorization(PolicyWrite);

        group.MapPost("/{id:guid}/disable", async (
            Guid id,
            ICommandHandler<DisableUserCommand> handler,
            CancellationToken ct) =>
        {
            Result result = await handler.Handle(new DisableUserCommand(id), ct);
            return result.Match(Results.NoContent, CustomResults.Problem);
        }).RequireAuthorization(PolicyWrite);

        group.MapPost("/{id:guid}/enable", async (
            Guid id,
            ICommandHandler<EnableUserCommand> handler,
            CancellationToken ct) =>
        {
            Result result = await handler.Handle(new EnableUserCommand(id), ct);
            return result.Match(Results.NoContent, CustomResults.Problem);
        }).RequireAuthorization(PolicyWrite);

        group.MapPatch("/{id:guid}/netsuite-email", async (
            Guid id,
            SetNetSuiteEmailRequest req,
            ICommandHandler<SetNetSuiteEmailCommand> handler,
            CancellationToken ct) =>
        {
            Result result = await handler.Handle(
                new SetNetSuiteEmailCommand(id, req.NetSuiteEmail), ct);
            return result.Match(Results.NoContent, CustomResults.Problem);
        }).RequireAuthorization(PolicyWrite);

        group.MapPost("/{userId:guid}/roles/{roleId:guid}", async (
            Guid userId,
            Guid roleId,
            ICommandHandler<AssignRoleToUserCommand> handler,
            CancellationToken ct) =>
        {
            Result result = await handler.Handle(new AssignRoleToUserCommand(userId, roleId), ct);
            return result.Match(Results.NoContent, CustomResults.Problem);
        }).RequireAuthorization(PolicyWrite);

        group.MapDelete("/{userId:guid}/roles/{roleId:guid}", async (
            Guid userId,
            Guid roleId,
            ICommandHandler<RevokeRoleFromUserCommand> handler,
            CancellationToken ct) =>
        {
            Result result = await handler.Handle(new RevokeRoleFromUserCommand(userId, roleId), ct);
            return result.Match(Results.NoContent, CustomResults.Problem);
        }).RequireAuthorization(PolicyWrite);

        group.MapPost("/{userId:guid}/groups/{groupId:guid}", async (
            Guid userId,
            Guid groupId,
            ICommandHandler<AddUserToGroupCommand> handler,
            CancellationToken ct) =>
        {
            Result result = await handler.Handle(new AddUserToGroupCommand(userId, groupId), ct);
            return result.Match(Results.NoContent, CustomResults.Problem);
        }).RequireAuthorization(PolicyWrite);

        group.MapDelete("/{userId:guid}/groups/{groupId:guid}", async (
            Guid userId,
            Guid groupId,
            ICommandHandler<RemoveUserFromGroupCommand> handler,
            CancellationToken ct) =>
        {
            Result result = await handler.Handle(
                new RemoveUserFromGroupCommand(userId, groupId), ct);
            return result.Match(Results.NoContent, CustomResults.Problem);
        }).RequireAuthorization(PolicyWrite);
    }

    private sealed record SetNetSuiteEmailRequest(string? NetSuiteEmail);
}

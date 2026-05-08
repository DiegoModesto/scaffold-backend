using Auth.API.Authorization;
using Auth.API.Extensions;
using Auth.API.Infrastructure;
using Auth.Application.Abstractions.Messaging;
using Auth.Application.Admin.M2MClients.Create;
using Auth.Application.Admin.M2MClients.Deactivate;
using Auth.Application.Admin.M2MClients.GetM2MClient;
using Auth.Application.Admin.M2MClients.ListM2MClients;
using Auth.Application.Admin.M2MClients.RegenerateSecret;
using Auth.Application.Common;
using Auth.Domain.Permissions;
using SharedKernel;

namespace Auth.API.Endpoints.Admin;

internal sealed class M2MClientsEndpoints : IEndpoint
{
    private const string PolicyRead = $"{PermissionPolicyProvider.PolicyPrefix}{PermissionCodes.M2MClientsRead}";
    private const string PolicyWrite = $"{PermissionPolicyProvider.PolicyPrefix}{PermissionCodes.M2MClientsWrite}";

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/admin/m2m-clients").WithTags("Admin: M2M Clients");

        group.MapGet("/", async (
            int? page,
            int? pageSize,
            string? search,
            IQueryHandler<ListM2MClientsQuery, PagedResponse<M2MClientSummary>> handler,
            CancellationToken ct) =>
        {
            Result<PagedResponse<M2MClientSummary>> result = await handler.Handle(
                new ListM2MClientsQuery(page ?? 1, pageSize ?? 20, search), ct);
            return result.Match(Results.Ok, CustomResults.Problem);
        }).RequireAuthorization(PolicyRead);

        group.MapGet("/{id:guid}", async (
            Guid id,
            IQueryHandler<GetM2MClientByIdQuery, M2MClientDetailResponse> handler,
            CancellationToken ct) =>
        {
            Result<M2MClientDetailResponse> result = await handler.Handle(
                new GetM2MClientByIdQuery(id), ct);
            return result.Match(Results.Ok, CustomResults.Problem);
        }).RequireAuthorization(PolicyRead);

        group.MapPost("/", async (
            CreateM2MClientCommand cmd,
            ICommandHandler<CreateM2MClientCommand, CreateM2MClientResponse> handler,
            CancellationToken ct) =>
        {
            Result<CreateM2MClientResponse> result = await handler.Handle(cmd, ct);
            return result.Match(
                response => Results.Created($"/admin/m2m-clients/{response.Id}", response),
                CustomResults.Problem);
        }).RequireAuthorization(PolicyWrite);

        group.MapPost("/{id:guid}/regenerate-secret", async (
            Guid id,
            ICommandHandler<RegenerateM2MClientSecretCommand, string> handler,
            CancellationToken ct) =>
        {
            Result<string> result = await handler.Handle(
                new RegenerateM2MClientSecretCommand(id), ct);
            return result.Match(
                secret => Results.Ok(new { clientSecret = secret }),
                CustomResults.Problem);
        }).RequireAuthorization(PolicyWrite);

        group.MapPost("/{id:guid}/deactivate", async (
            Guid id,
            ICommandHandler<DeactivateM2MClientCommand> handler,
            CancellationToken ct) =>
        {
            Result result = await handler.Handle(new DeactivateM2MClientCommand(id), ct);
            return result.Match(Results.NoContent, CustomResults.Problem);
        }).RequireAuthorization(PolicyWrite);
    }
}

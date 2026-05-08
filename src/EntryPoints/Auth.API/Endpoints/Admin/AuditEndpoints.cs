using Infra.Authorization;
using Auth.API.Extensions;
using Auth.API.Infrastructure;
using Auth.Application.Abstractions.Messaging;
using Auth.Application.Admin.Audit.ListAuditEvents;
using Auth.Application.Common;
using Auth.Domain.Audit;
using Auth.Domain.Permissions;
using SharedKernel;

namespace Auth.API.Endpoints.Admin;

internal sealed class AuditEndpoints : IEndpoint
{
    private const string PolicyRead = $"{PermissionPolicyProvider.PolicyPrefix}{PermissionCodes.AuditRead}";

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/admin/audit").WithTags("Admin: Audit");

        group.MapGet("/", async (
            int? page,
            int? pageSize,
            Guid? userId,
            AuthAuditEventType? eventType,
            DateTimeOffset? from,
            DateTimeOffset? to,
            IQueryHandler<ListAuditEventsQuery, PagedResponse<AuthAuditEventResponse>> handler,
            CancellationToken ct) =>
        {
            Result<PagedResponse<AuthAuditEventResponse>> result = await handler.Handle(
                new ListAuditEventsQuery(
                    page ?? 1,
                    pageSize ?? 20,
                    userId,
                    eventType,
                    from,
                    to),
                ct);
            return result.Match(Results.Ok, CustomResults.Problem);
        }).RequireAuthorization(PolicyRead);
    }
}

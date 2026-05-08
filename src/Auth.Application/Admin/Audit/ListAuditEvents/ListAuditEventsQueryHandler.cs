using Auth.Application.Abstractions.Data;
using Auth.Application.Abstractions.Messaging;
using Auth.Application.Abstractions.Tenancy;
using Auth.Application.Common;
using Auth.Domain.Audit;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Auth.Application.Admin.Audit.ListAuditEvents;

public sealed class ListAuditEventsQueryHandler(IAuthDbContext db, ITenantContext tenant)
    : IQueryHandler<ListAuditEventsQuery, PagedResponse<AuthAuditEventResponse>>
{
    public async Task<Result<PagedResponse<AuthAuditEventResponse>>> Handle(
        ListAuditEventsQuery query,
        CancellationToken cancellationToken)
    {
        int page = query.Page < 1 ? 1 : query.Page;
        int pageSize = query.PageSize is < 1 or > 200 ? 50 : query.PageSize;
        Guid tenantId = tenant.TenantId;

        IQueryable<AuthAuditEvent> q = db.AuditEvents.Where(e => e.TenantId == tenantId);

        if (query.UserId is { } userId)
        {
            q = q.Where(e => e.UserId == userId);
        }

        if (query.EventType is { } eventType)
        {
            q = q.Where(e => e.EventType == eventType);
        }

        if (query.From is { } from)
        {
            q = q.Where(e => e.OccurredAt >= from);
        }

        if (query.To is { } to)
        {
            q = q.Where(e => e.OccurredAt <= to);
        }

        int total = await q.CountAsync(cancellationToken);

        List<AuthAuditEventResponse> items = await q
            .OrderByDescending(e => e.OccurredAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(e => new AuthAuditEventResponse(
                e.Id,
                e.UserId,
                e.EventType,
                e.Ip,
                e.UserAgent,
                e.Detail,
                e.OccurredAt))
            .ToListAsync(cancellationToken);

        return new PagedResponse<AuthAuditEventResponse>(items, page, pageSize, total);
    }
}

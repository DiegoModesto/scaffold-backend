using Auth.Application.Abstractions.Data;
using Auth.Application.Abstractions.Messaging;
using Auth.Application.Abstractions.Tenancy;
using Auth.Application.Common;
using Auth.Domain.M2MClients;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Auth.Application.Admin.M2MClients.ListM2MClients;

public sealed class ListM2MClientsQueryHandler(IAuthDbContext db, ITenantContext tenant)
    : IQueryHandler<ListM2MClientsQuery, PagedResponse<M2MClientSummary>>
{
    public async Task<Result<PagedResponse<M2MClientSummary>>> Handle(
        ListM2MClientsQuery query,
        CancellationToken cancellationToken)
    {
        int page = query.Page < 1 ? 1 : query.Page;
        int pageSize = query.PageSize is < 1 or > 200 ? 50 : query.PageSize;
        Guid tenantId = tenant.TenantId;

        IQueryable<M2MClient> q = db.M2MClients.Where(c => c.TenantId == tenantId && !c.IsDeleted);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            string s = query.Search.Trim();
            q = q.Where(c => c.ClientId.Contains(s) || c.DisplayName.Contains(s));
        }

        int total = await q.CountAsync(cancellationToken);

        List<M2MClient> rows = await q
            .OrderBy(c => c.ClientId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(c => new M2MClientSummary(c.Id, c.ClientId, c.DisplayName, c.IsActive, [.. c.AllowedScopes]))
            .ToList();

        return new PagedResponse<M2MClientSummary>(items, page, pageSize, total);
    }
}

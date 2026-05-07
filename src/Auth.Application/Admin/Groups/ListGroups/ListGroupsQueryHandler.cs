using Auth.Application.Abstractions.Data;
using Auth.Application.Abstractions.Messaging;
using Auth.Application.Abstractions.Tenancy;
using Auth.Application.Common;
using Auth.Domain.Groups;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Auth.Application.Admin.Groups.ListGroups;

public sealed class ListGroupsQueryHandler(IAuthDbContext db, ITenantContext tenant)
    : IQueryHandler<ListGroupsQuery, PagedResponse<GroupSummary>>
{
    public async Task<Result<PagedResponse<GroupSummary>>> Handle(
        ListGroupsQuery query,
        CancellationToken cancellationToken)
    {
        int page = query.Page < 1 ? 1 : query.Page;
        int pageSize = query.PageSize is < 1 or > 200 ? 50 : query.PageSize;
        Guid tenantId = tenant.TenantId;

        IQueryable<Group> q = db.Groups.Where(g => g.TenantId == tenantId && !g.IsDeleted);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            string s = query.Search.Trim();
            q = q.Where(g => g.Name.Contains(s) || g.Description.Contains(s));
        }

        int total = await q.CountAsync(cancellationToken);

        List<GroupSummary> items = await q
            .OrderBy(g => g.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(g => new GroupSummary(g.Id, g.Name, g.Description, g.EntraGroupId))
            .ToListAsync(cancellationToken);

        return new PagedResponse<GroupSummary>(items, page, pageSize, total);
    }
}

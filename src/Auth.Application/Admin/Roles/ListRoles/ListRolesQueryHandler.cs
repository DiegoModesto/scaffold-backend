using Auth.Application.Abstractions.Data;
using Auth.Application.Abstractions.Messaging;
using Auth.Application.Abstractions.Tenancy;
using Auth.Application.Common;
using Auth.Domain.Roles;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Auth.Application.Admin.Roles.ListRoles;

public sealed class ListRolesQueryHandler(IAuthDbContext db, ITenantContext tenant)
    : IQueryHandler<ListRolesQuery, PagedResponse<RoleSummary>>
{
    public async Task<Result<PagedResponse<RoleSummary>>> Handle(
        ListRolesQuery query,
        CancellationToken cancellationToken)
    {
        int page = query.Page < 1 ? 1 : query.Page;
        int pageSize = query.PageSize is < 1 or > 200 ? 50 : query.PageSize;
        Guid tenantId = tenant.TenantId;

        IQueryable<Role> q = db.Roles.Where(r => r.TenantId == tenantId && !r.IsDeleted);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            string s = query.Search.Trim();
            q = q.Where(r => r.Name.Contains(s) || r.Description.Contains(s));
        }

        int total = await q.CountAsync(cancellationToken);

        List<RoleSummary> items = await q
            .OrderBy(r => r.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(r => new RoleSummary(r.Id, r.Name, r.Description))
            .ToListAsync(cancellationToken);

        return new PagedResponse<RoleSummary>(items, page, pageSize, total);
    }
}

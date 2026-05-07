using Auth.Application.Abstractions.Data;
using Auth.Application.Abstractions.Messaging;
using Auth.Application.Abstractions.Tenancy;
using Auth.Application.Common;
using Auth.Domain.Users;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Auth.Application.Admin.Users.ListUsers;

public sealed class ListUsersQueryHandler(IAuthDbContext db, ITenantContext tenant)
    : IQueryHandler<ListUsersQuery, PagedResponse<UserSummary>>
{
    public async Task<Result<PagedResponse<UserSummary>>> Handle(
        ListUsersQuery query,
        CancellationToken cancellationToken)
    {
        int page = query.Page < 1 ? 1 : query.Page;
        int pageSize = query.PageSize is < 1 or > 200 ? 50 : query.PageSize;
        Guid tenantId = tenant.TenantId;

        IQueryable<User> q = db.Users.Where(u => u.TenantId == tenantId && !u.IsDeleted);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            string s = query.Search.Trim();
            q = q.Where(u => u.Email.Contains(s) || u.DisplayName.Contains(s));
        }

        int total = await q.CountAsync(cancellationToken);

        List<UserSummary> items = await q
            .OrderBy(u => u.Email)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(u => new UserSummary(u.Id, u.Email, u.DisplayName, u.IsActive))
            .ToListAsync(cancellationToken);

        return new PagedResponse<UserSummary>(items, page, pageSize, total);
    }
}

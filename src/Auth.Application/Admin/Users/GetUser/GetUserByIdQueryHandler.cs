using Auth.Application.Abstractions.Data;
using Auth.Application.Abstractions.Messaging;
using Auth.Application.Abstractions.Tenancy;
using Auth.Domain.Users;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Auth.Application.Admin.Users.GetUser;

public sealed class GetUserByIdQueryHandler(IAuthDbContext db, ITenantContext tenant)
    : IQueryHandler<GetUserByIdQuery, UserDetailResponse>
{
    public async Task<Result<UserDetailResponse>> Handle(
        GetUserByIdQuery query,
        CancellationToken cancellationToken)
    {
        Guid tenantId = tenant.TenantId;

        User? user = await db.Users
            .FirstOrDefaultAsync(
                u => u.Id == query.Id && u.TenantId == tenantId && !u.IsDeleted,
                cancellationToken);

        if (user is null)
        {
            return Result.Failure<UserDetailResponse>(UserErrors.NotFound(query.Id));
        }

        // Look up role + group names so the UI doesn't have to round-trip per id.
        Guid[] roleIds = [.. user.RoleIds];
        Guid[] groupIds = [.. user.GroupIds];

        List<RoleRef> roles = await db.Roles
            .Where(r => roleIds.Contains(r.Id) && r.TenantId == tenantId && !r.IsDeleted)
            .Select(r => new RoleRef(r.Id, r.Name))
            .ToListAsync(cancellationToken);

        List<GroupRef> groups = await db.Groups
            .Where(g => groupIds.Contains(g.Id) && g.TenantId == tenantId && !g.IsDeleted)
            .Select(g => new GroupRef(g.Id, g.Name))
            .ToListAsync(cancellationToken);

        return new UserDetailResponse(
            user.Id,
            user.Email,
            user.DisplayName,
            user.NetSuiteEmail,
            user.IsActive,
            user.IsPreProvisioned,
            user.EntraOid,
            user.LastLoginAt,
            roles,
            groups);
    }
}

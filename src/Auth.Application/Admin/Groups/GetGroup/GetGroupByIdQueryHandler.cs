using Auth.Application.Abstractions.Data;
using Auth.Application.Abstractions.Messaging;
using Auth.Application.Abstractions.Tenancy;
using Auth.Domain.Groups;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Auth.Application.Admin.Groups.GetGroup;

public sealed class GetGroupByIdQueryHandler(IAuthDbContext db, ITenantContext tenant)
    : IQueryHandler<GetGroupByIdQuery, GroupDetailResponse>
{
    public async Task<Result<GroupDetailResponse>> Handle(
        GetGroupByIdQuery query,
        CancellationToken cancellationToken)
    {
        Guid tenantId = tenant.TenantId;
        Group? group = await db.Groups.FirstOrDefaultAsync(
            g => g.Id == query.Id && g.TenantId == tenantId && !g.IsDeleted,
            cancellationToken);

        if (group is null)
        {
            return Result.Failure<GroupDetailResponse>(GroupErrors.NotFound(query.Id));
        }

        Guid[] roleIds = [.. group.RoleIds];
        List<GroupRoleRef> roles = await db.Roles
            .Where(r => roleIds.Contains(r.Id) && r.TenantId == tenantId && !r.IsDeleted)
            .Select(r => new GroupRoleRef(r.Id, r.Name))
            .ToListAsync(cancellationToken);

        return new GroupDetailResponse(group.Id, group.Name, group.Description, group.EntraGroupId, roles);
    }
}

using Auth.Application.Abstractions.Data;
using Auth.Application.Abstractions.Messaging;
using Auth.Application.Abstractions.Tenancy;
using Auth.Domain.Roles;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Auth.Application.Admin.Roles.GetRole;

public sealed class GetRoleByIdQueryHandler(IAuthDbContext db, ITenantContext tenant)
    : IQueryHandler<GetRoleByIdQuery, RoleDetailResponse>
{
    public async Task<Result<RoleDetailResponse>> Handle(
        GetRoleByIdQuery query,
        CancellationToken cancellationToken)
    {
        Guid tenantId = tenant.TenantId;
        Role? role = await db.Roles.FirstOrDefaultAsync(
            r => r.Id == query.Id && r.TenantId == tenantId && !r.IsDeleted,
            cancellationToken);

        if (role is null)
        {
            return Result.Failure<RoleDetailResponse>(RoleErrors.NotFound(query.Id));
        }

        Guid[] permissionIds = [.. role.PermissionIds];
        List<RolePermissionRef> permissions = await db.Permissions
            .Where(p => permissionIds.Contains(p.Id))
            .Select(p => new RolePermissionRef(p.Id, p.Code, p.Description))
            .ToListAsync(cancellationToken);

        return new RoleDetailResponse(role.Id, role.Name, role.Description, permissions);
    }
}

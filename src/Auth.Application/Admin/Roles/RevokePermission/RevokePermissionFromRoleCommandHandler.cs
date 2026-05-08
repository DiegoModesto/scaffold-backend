using Auth.Application.Abstractions.Data;
using Auth.Application.Abstractions.Messaging;
using Auth.Application.Abstractions.Tenancy;
using Auth.Domain.Roles;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Auth.Application.Admin.Roles.RevokePermission;

public sealed class RevokePermissionFromRoleCommandHandler(IAuthDbContext db, ITenantContext tenant)
    : ICommandHandler<RevokePermissionFromRoleCommand>
{
    public async Task<Result> Handle(
        RevokePermissionFromRoleCommand command,
        CancellationToken cancellationToken)
    {
        Guid tenantId = tenant.TenantId;
        Role? role = await db.Roles.FirstOrDefaultAsync(
            r => r.Id == command.RoleId && r.TenantId == tenantId && !r.IsDeleted,
            cancellationToken);

        if (role is null)
        {
            return Result.Failure(RoleErrors.NotFound(command.RoleId));
        }

        role.RevokePermission(command.PermissionId);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

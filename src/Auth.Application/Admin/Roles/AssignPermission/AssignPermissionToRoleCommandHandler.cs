using Auth.Application.Abstractions.Data;
using Auth.Application.Abstractions.Messaging;
using Auth.Application.Abstractions.Tenancy;
using Auth.Domain.Permissions;
using Auth.Domain.Roles;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Auth.Application.Admin.Roles.AssignPermission;

public sealed class AssignPermissionToRoleCommandHandler(IAuthDbContext db, ITenantContext tenant)
    : ICommandHandler<AssignPermissionToRoleCommand>
{
    public async Task<Result> Handle(
        AssignPermissionToRoleCommand command,
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

        bool permissionExists = await db.Permissions.AnyAsync(
            p => p.Id == command.PermissionId,
            cancellationToken);

        if (!permissionExists)
        {
            return Result.Failure(PermissionErrors.NotFound(command.PermissionId));
        }

        role.AssignPermission(command.PermissionId);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

using Auth.Application.Abstractions.Data;
using Auth.Application.Abstractions.Messaging;
using Auth.Application.Abstractions.Tenancy;
using Auth.Domain.Groups;
using Auth.Domain.Roles;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Auth.Application.Admin.Groups.AssignRole;

public sealed class AssignRoleToGroupCommandHandler(IAuthDbContext db, ITenantContext tenant)
    : ICommandHandler<AssignRoleToGroupCommand>
{
    public async Task<Result> Handle(AssignRoleToGroupCommand command, CancellationToken cancellationToken)
    {
        Guid tenantId = tenant.TenantId;

        Group? group = await db.Groups.FirstOrDefaultAsync(
            g => g.Id == command.GroupId && g.TenantId == tenantId && !g.IsDeleted,
            cancellationToken);

        if (group is null)
        {
            return Result.Failure(GroupErrors.NotFound(command.GroupId));
        }

        bool roleExists = await db.Roles.AnyAsync(
            r => r.Id == command.RoleId && r.TenantId == tenantId && !r.IsDeleted,
            cancellationToken);

        if (!roleExists)
        {
            return Result.Failure(RoleErrors.NotFound(command.RoleId));
        }

        group.AssignRole(command.RoleId);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

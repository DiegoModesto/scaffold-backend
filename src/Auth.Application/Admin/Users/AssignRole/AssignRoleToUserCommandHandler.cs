using Auth.Application.Abstractions.Data;
using Auth.Application.Abstractions.Messaging;
using Auth.Application.Abstractions.Tenancy;
using Auth.Domain.Roles;
using Auth.Domain.Users;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Auth.Application.Admin.Users.AssignRole;

public sealed class AssignRoleToUserCommandHandler(IAuthDbContext db, ITenantContext tenant)
    : ICommandHandler<AssignRoleToUserCommand>
{
    public async Task<Result> Handle(AssignRoleToUserCommand command, CancellationToken cancellationToken)
    {
        Guid tenantId = tenant.TenantId;

        User? user = await db.Users.FirstOrDefaultAsync(
            u => u.Id == command.UserId && u.TenantId == tenantId && !u.IsDeleted,
            cancellationToken);

        if (user is null)
        {
            return Result.Failure(UserErrors.NotFound(command.UserId));
        }

        bool roleExists = await db.Roles.AnyAsync(
            r => r.Id == command.RoleId && r.TenantId == tenantId && !r.IsDeleted,
            cancellationToken);

        if (!roleExists)
        {
            return Result.Failure(RoleErrors.NotFound(command.RoleId));
        }

        user.AssignRole(command.RoleId);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

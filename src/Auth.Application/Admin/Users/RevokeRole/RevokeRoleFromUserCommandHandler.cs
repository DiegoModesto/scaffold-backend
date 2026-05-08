using Auth.Application.Abstractions.Data;
using Auth.Application.Abstractions.Messaging;
using Auth.Application.Abstractions.Tenancy;
using Auth.Domain.Users;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Auth.Application.Admin.Users.RevokeRole;

public sealed class RevokeRoleFromUserCommandHandler(IAuthDbContext db, ITenantContext tenant)
    : ICommandHandler<RevokeRoleFromUserCommand>
{
    public async Task<Result> Handle(RevokeRoleFromUserCommand command, CancellationToken cancellationToken)
    {
        Guid tenantId = tenant.TenantId;
        User? user = await db.Users.FirstOrDefaultAsync(
            u => u.Id == command.UserId && u.TenantId == tenantId && !u.IsDeleted,
            cancellationToken);

        if (user is null)
        {
            return Result.Failure(UserErrors.NotFound(command.UserId));
        }

        user.RevokeRole(command.RoleId);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

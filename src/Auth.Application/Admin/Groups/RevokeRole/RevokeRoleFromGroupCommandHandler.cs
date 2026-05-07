using Auth.Application.Abstractions.Data;
using Auth.Application.Abstractions.Messaging;
using Auth.Application.Abstractions.Tenancy;
using Auth.Domain.Groups;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Auth.Application.Admin.Groups.RevokeRole;

public sealed class RevokeRoleFromGroupCommandHandler(IAuthDbContext db, ITenantContext tenant)
    : ICommandHandler<RevokeRoleFromGroupCommand>
{
    public async Task<Result> Handle(RevokeRoleFromGroupCommand command, CancellationToken cancellationToken)
    {
        Guid tenantId = tenant.TenantId;
        Group? group = await db.Groups.FirstOrDefaultAsync(
            g => g.Id == command.GroupId && g.TenantId == tenantId && !g.IsDeleted,
            cancellationToken);

        if (group is null)
        {
            return Result.Failure(GroupErrors.NotFound(command.GroupId));
        }

        group.RevokeRole(command.RoleId);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

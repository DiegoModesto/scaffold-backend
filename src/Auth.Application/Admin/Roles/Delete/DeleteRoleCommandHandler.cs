using Auth.Application.Abstractions.Data;
using Auth.Application.Abstractions.Messaging;
using Auth.Application.Abstractions.Tenancy;
using Auth.Domain.Roles;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Auth.Application.Admin.Roles.Delete;

public sealed class DeleteRoleCommandHandler(IAuthDbContext db, ITenantContext tenant)
    : ICommandHandler<DeleteRoleCommand>
{
    public async Task<Result> Handle(DeleteRoleCommand command, CancellationToken cancellationToken)
    {
        Guid tenantId = tenant.TenantId;
        Role? role = await db.Roles.FirstOrDefaultAsync(
            r => r.Id == command.Id && r.TenantId == tenantId && !r.IsDeleted,
            cancellationToken);

        if (role is null)
        {
            return Result.Failure(RoleErrors.NotFound(command.Id));
        }

        role.Delete();
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

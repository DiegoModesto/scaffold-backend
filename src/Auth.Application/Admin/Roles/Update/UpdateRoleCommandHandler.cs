using Auth.Application.Abstractions.Data;
using Auth.Application.Abstractions.Messaging;
using Auth.Application.Abstractions.Tenancy;
using Auth.Domain.Roles;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Auth.Application.Admin.Roles.Update;

public sealed class UpdateRoleCommandHandler(IAuthDbContext db, ITenantContext tenant)
    : ICommandHandler<UpdateRoleCommand>
{
    public async Task<Result> Handle(UpdateRoleCommand command, CancellationToken cancellationToken)
    {
        Guid tenantId = tenant.TenantId;
        Role? role = await db.Roles.FirstOrDefaultAsync(
            r => r.Id == command.Id && r.TenantId == tenantId && !r.IsDeleted,
            cancellationToken);

        if (role is null)
        {
            return Result.Failure(RoleErrors.NotFound(command.Id));
        }

        if (role.Name != command.Name)
        {
            bool taken = await db.Roles.AnyAsync(
                r => r.TenantId == tenantId && r.Name == command.Name && r.Id != command.Id && !r.IsDeleted,
                cancellationToken);
            if (taken)
            {
                return Result.Failure(RoleErrors.NameAlreadyTaken);
            }
        }

        role.UpdateDetails(command.Name, command.Description);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

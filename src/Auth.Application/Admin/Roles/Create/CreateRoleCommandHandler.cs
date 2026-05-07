using Auth.Application.Abstractions.Data;
using Auth.Application.Abstractions.Messaging;
using Auth.Application.Abstractions.Tenancy;
using Auth.Domain.Roles;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Auth.Application.Admin.Roles.Create;

public sealed class CreateRoleCommandHandler(IAuthDbContext db, ITenantContext tenant)
    : ICommandHandler<CreateRoleCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateRoleCommand command, CancellationToken cancellationToken)
    {
        Guid tenantId = tenant.TenantId;

        bool exists = await db.Roles.AnyAsync(
            r => r.TenantId == tenantId && r.Name == command.Name && !r.IsDeleted,
            cancellationToken);

        if (exists)
        {
            return Result.Failure<Guid>(RoleErrors.NameAlreadyTaken);
        }

        Role role = Role.Create(tenantId, command.Name, command.Description);
        db.Roles.Add(role);
        await db.SaveChangesAsync(cancellationToken);
        return role.Id;
    }
}

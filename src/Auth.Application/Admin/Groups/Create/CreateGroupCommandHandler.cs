using Auth.Application.Abstractions.Data;
using Auth.Application.Abstractions.Messaging;
using Auth.Application.Abstractions.Tenancy;
using Auth.Domain.Groups;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Auth.Application.Admin.Groups.Create;

public sealed class CreateGroupCommandHandler(IAuthDbContext db, ITenantContext tenant)
    : ICommandHandler<CreateGroupCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateGroupCommand command, CancellationToken cancellationToken)
    {
        Guid tenantId = tenant.TenantId;

        bool exists = await db.Groups.AnyAsync(
            g => g.TenantId == tenantId && g.Name == command.Name && !g.IsDeleted,
            cancellationToken);

        if (exists)
        {
            return Result.Failure<Guid>(GroupErrors.NameAlreadyTaken);
        }

        Group group = Group.Create(tenantId, command.Name, command.Description);
        if (command.EntraGroupId is { } entra)
        {
            group.LinkEntraGroup(entra);
        }

        db.Groups.Add(group);
        await db.SaveChangesAsync(cancellationToken);
        return group.Id;
    }
}

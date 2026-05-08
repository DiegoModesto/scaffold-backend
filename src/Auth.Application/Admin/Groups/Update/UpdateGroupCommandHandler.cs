using Auth.Application.Abstractions.Data;
using Auth.Application.Abstractions.Messaging;
using Auth.Application.Abstractions.Tenancy;
using Auth.Domain.Groups;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Auth.Application.Admin.Groups.Update;

public sealed class UpdateGroupCommandHandler(IAuthDbContext db, ITenantContext tenant)
    : ICommandHandler<UpdateGroupCommand>
{
    public async Task<Result> Handle(UpdateGroupCommand command, CancellationToken cancellationToken)
    {
        Guid tenantId = tenant.TenantId;
        Group? group = await db.Groups.FirstOrDefaultAsync(
            g => g.Id == command.Id && g.TenantId == tenantId && !g.IsDeleted,
            cancellationToken);

        if (group is null)
        {
            return Result.Failure(GroupErrors.NotFound(command.Id));
        }

        if (group.Name != command.Name)
        {
            bool nameTaken = await db.Groups.AnyAsync(
                g => g.TenantId == tenantId && g.Name == command.Name && g.Id != command.Id && !g.IsDeleted,
                cancellationToken);
            if (nameTaken)
            {
                return Result.Failure(GroupErrors.NameAlreadyTaken);
            }
        }

        group.UpdateDetails(command.Name, command.Description, command.EntraGroupId);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

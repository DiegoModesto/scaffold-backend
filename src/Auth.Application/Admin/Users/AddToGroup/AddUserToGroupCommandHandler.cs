using Auth.Application.Abstractions.Data;
using Auth.Application.Abstractions.Messaging;
using Auth.Application.Abstractions.Tenancy;
using Auth.Domain.Groups;
using Auth.Domain.Users;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Auth.Application.Admin.Users.AddToGroup;

public sealed class AddUserToGroupCommandHandler(IAuthDbContext db, ITenantContext tenant)
    : ICommandHandler<AddUserToGroupCommand>
{
    public async Task<Result> Handle(AddUserToGroupCommand command, CancellationToken cancellationToken)
    {
        Guid tenantId = tenant.TenantId;

        User? user = await db.Users.FirstOrDefaultAsync(
            u => u.Id == command.UserId && u.TenantId == tenantId && !u.IsDeleted,
            cancellationToken);

        if (user is null)
        {
            return Result.Failure(UserErrors.NotFound(command.UserId));
        }

        bool groupExists = await db.Groups.AnyAsync(
            g => g.Id == command.GroupId && g.TenantId == tenantId && !g.IsDeleted,
            cancellationToken);

        if (!groupExists)
        {
            return Result.Failure(GroupErrors.NotFound(command.GroupId));
        }

        user.AddToGroup(command.GroupId);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

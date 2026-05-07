using Auth.Application.Abstractions.Data;
using Auth.Application.Abstractions.Messaging;
using Auth.Application.Abstractions.Tenancy;
using Auth.Domain.Users;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Auth.Application.Admin.Users.Disable;

public sealed class DisableUserCommandHandler(IAuthDbContext db, ITenantContext tenant)
    : ICommandHandler<DisableUserCommand>
{
    public async Task<Result> Handle(DisableUserCommand command, CancellationToken cancellationToken)
    {
        Guid tenantId = tenant.TenantId;
        User? user = await db.Users.FirstOrDefaultAsync(
            u => u.Id == command.Id && u.TenantId == tenantId && !u.IsDeleted,
            cancellationToken);

        if (user is null)
        {
            return Result.Failure(UserErrors.NotFound(command.Id));
        }

        user.Disable();
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

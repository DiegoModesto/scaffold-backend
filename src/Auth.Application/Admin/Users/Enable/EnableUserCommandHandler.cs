using Auth.Application.Abstractions.Data;
using Auth.Application.Abstractions.Messaging;
using Auth.Application.Abstractions.Tenancy;
using Auth.Domain.Users;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Auth.Application.Admin.Users.Enable;

public sealed class EnableUserCommandHandler(IAuthDbContext db, ITenantContext tenant)
    : ICommandHandler<EnableUserCommand>
{
    public async Task<Result> Handle(EnableUserCommand command, CancellationToken cancellationToken)
    {
        Guid tenantId = tenant.TenantId;
        User? user = await db.Users.FirstOrDefaultAsync(
            u => u.Id == command.Id && u.TenantId == tenantId && !u.IsDeleted,
            cancellationToken);

        if (user is null)
        {
            return Result.Failure(UserErrors.NotFound(command.Id));
        }

        user.Enable();
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

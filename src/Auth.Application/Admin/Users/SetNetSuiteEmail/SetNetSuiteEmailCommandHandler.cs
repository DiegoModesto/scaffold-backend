using Auth.Application.Abstractions.Data;
using Auth.Application.Abstractions.Messaging;
using Auth.Application.Abstractions.Tenancy;
using Auth.Domain.Users;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Auth.Application.Admin.Users.SetNetSuiteEmail;

public sealed class SetNetSuiteEmailCommandHandler(IAuthDbContext db, ITenantContext tenant)
    : ICommandHandler<SetNetSuiteEmailCommand>
{
    public async Task<Result> Handle(SetNetSuiteEmailCommand command, CancellationToken cancellationToken)
    {
        Guid tenantId = tenant.TenantId;
        User? user = await db.Users.FirstOrDefaultAsync(
            u => u.Id == command.Id && u.TenantId == tenantId && !u.IsDeleted,
            cancellationToken);

        if (user is null)
        {
            return Result.Failure(UserErrors.NotFound(command.Id));
        }

        user.SetNetSuiteEmail(command.NetSuiteEmail);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

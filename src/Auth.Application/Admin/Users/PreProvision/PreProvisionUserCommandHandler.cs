using Auth.Application.Abstractions.Data;
using Auth.Application.Abstractions.Messaging;
using Auth.Application.Abstractions.Tenancy;
using Auth.Domain.Users;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Auth.Application.Admin.Users.PreProvision;

public sealed class PreProvisionUserCommandHandler(IAuthDbContext db, ITenantContext tenant)
    : ICommandHandler<PreProvisionUserCommand, Guid>
{
    public async Task<Result<Guid>> Handle(
        PreProvisionUserCommand command,
        CancellationToken cancellationToken)
    {
        Guid tenantId = tenant.TenantId;

        bool exists = await db.Users.AnyAsync(
            u => u.TenantId == tenantId && u.Email == command.Email && !u.IsDeleted,
            cancellationToken);

        if (exists)
        {
            return Result.Failure<Guid>(UserErrors.EmailAlreadyTaken);
        }

        User user = User.PreProvision(tenantId, command.Email, command.DisplayName);
        if (!string.IsNullOrEmpty(command.NetSuiteEmail))
        {
            user.SetNetSuiteEmail(command.NetSuiteEmail);
        }

        db.Users.Add(user);
        await db.SaveChangesAsync(cancellationToken);
        return user.Id;
    }
}

using Auth.Application.Abstractions.Data;
using Auth.Application.Abstractions.Messaging;
using Auth.Domain.Users;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Auth.Application.Users.SyncEntra;

public sealed class SyncEntraUserCommandHandler(IAuthDbContext db)
    : ICommandHandler<SyncEntraUserCommand, Guid>
{
    public async Task<Result<Guid>> Handle(SyncEntraUserCommand command, CancellationToken cancellationToken)
    {
        User? byOid = await db.Users
            .FirstOrDefaultAsync(
                u => u.TenantId == command.TenantId && u.EntraOid == command.EntraOid,
                cancellationToken);

        if (byOid is not null)
        {
            if (!byOid.IsActive)
            {
                return Result.Failure<Guid>(UserErrors.Disabled);
            }

            byOid.RecordLogin();
            await db.SaveChangesAsync(cancellationToken);
            return byOid.Id;
        }

        User? byEmail = await db.Users
            .FirstOrDefaultAsync(
                u => u.TenantId == command.TenantId && u.Email == command.Email,
                cancellationToken);

        if (byEmail is not null)
        {
            if (!byEmail.IsPreProvisioned)
            {
                return Result.Failure<Guid>(UserErrors.Disabled);
            }

            byEmail.ActivateFromEntra(command.EntraOid, command.DisplayName);
            byEmail.RecordLogin();
            await db.SaveChangesAsync(cancellationToken);
            return byEmail.Id;
        }

        var user = User.ProvisionFromEntra(command.TenantId, command.EntraOid, command.Email, command.DisplayName);
        user.RecordLogin();
        db.Users.Add(user);
        await db.SaveChangesAsync(cancellationToken);
        return user.Id;
    }
}

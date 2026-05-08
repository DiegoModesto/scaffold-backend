using Auth.Application.Abstractions.Data;
using Auth.Application.Abstractions.Messaging;
using Auth.Application.Abstractions.Tenancy;
using Auth.Domain.M2MClients;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using SharedKernel;

namespace Auth.Application.Admin.M2MClients.Deactivate;

public sealed class DeactivateM2MClientCommandHandler(
    IAuthDbContext db,
    ITenantContext tenant,
    IOpenIddictApplicationManager openIddictApps)
    : ICommandHandler<DeactivateM2MClientCommand>
{
    public async Task<Result> Handle(DeactivateM2MClientCommand command, CancellationToken cancellationToken)
    {
        Guid tenantId = tenant.TenantId;

        M2MClient? client = await db.M2MClients.FirstOrDefaultAsync(
            c => c.Id == command.Id && c.TenantId == tenantId && !c.IsDeleted,
            cancellationToken);

        if (client is null)
        {
            return Result.Failure(M2MClientErrors.NotFound(command.Id));
        }

        client.Deactivate();
        await db.SaveChangesAsync(cancellationToken);

        // Remove the OpenIddict mirror so existing tokens cannot be refreshed.
        object? app = await openIddictApps.FindByClientIdAsync(client.ClientId, cancellationToken);
        if (app is not null)
        {
            await openIddictApps.DeleteAsync(app, cancellationToken);
        }

        return Result.Success();
    }
}

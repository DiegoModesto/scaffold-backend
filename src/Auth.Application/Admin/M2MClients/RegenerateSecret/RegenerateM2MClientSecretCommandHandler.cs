using Auth.Application.Abstractions.Crypto;
using Auth.Application.Abstractions.Data;
using Auth.Application.Abstractions.Messaging;
using Auth.Application.Abstractions.Tenancy;
using Auth.Application.Admin.M2MClients.Common;
using Auth.Domain.M2MClients;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using SharedKernel;

namespace Auth.Application.Admin.M2MClients.RegenerateSecret;

public sealed class RegenerateM2MClientSecretCommandHandler(
    IAuthDbContext db,
    ITenantContext tenant,
    IClientSecretHasher hasher,
    IOpenIddictApplicationManager openIddictApps)
    : ICommandHandler<RegenerateM2MClientSecretCommand, string>
{
    public async Task<Result<string>> Handle(
        RegenerateM2MClientSecretCommand command,
        CancellationToken cancellationToken)
    {
        Guid tenantId = tenant.TenantId;

        M2MClient? client = await db.M2MClients.FirstOrDefaultAsync(
            c => c.Id == command.Id && c.TenantId == tenantId && !c.IsDeleted,
            cancellationToken);

        if (client is null)
        {
            return Result.Failure<string>(M2MClientErrors.NotFound(command.Id));
        }

        string plaintextSecret = SecretGenerator.Generate();
        client.RotateSecret(hasher.Hash(plaintextSecret));
        await db.SaveChangesAsync(cancellationToken);

        // Update OpenIddict mirror so old tokens stop and new clients use the new secret.
        object? app = await openIddictApps.FindByClientIdAsync(client.ClientId, cancellationToken);
        if (app is not null)
        {
            var descriptor = new OpenIddictApplicationDescriptor();
            await openIddictApps.PopulateAsync(descriptor, app, cancellationToken);
            descriptor.ClientSecret = plaintextSecret;
            await openIddictApps.UpdateAsync(app, descriptor, cancellationToken);
        }

        return plaintextSecret;
    }
}

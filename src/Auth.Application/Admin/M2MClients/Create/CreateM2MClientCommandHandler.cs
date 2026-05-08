using Auth.Application.Abstractions.Crypto;
using Auth.Application.Abstractions.Data;
using Auth.Application.Abstractions.Messaging;
using Auth.Application.Abstractions.Tenancy;
using Auth.Application.Admin.M2MClients.Common;
using Auth.Domain.M2MClients;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using SharedKernel;

namespace Auth.Application.Admin.M2MClients.Create;

public sealed class CreateM2MClientCommandHandler(
    IAuthDbContext db,
    ITenantContext tenant,
    IClientSecretHasher hasher,
    IOpenIddictApplicationManager openIddictApps)
    : ICommandHandler<CreateM2MClientCommand, CreateM2MClientResponse>
{
    public async Task<Result<CreateM2MClientResponse>> Handle(
        CreateM2MClientCommand command,
        CancellationToken cancellationToken)
    {
        Guid tenantId = tenant.TenantId;

        bool exists = await db.M2MClients.AnyAsync(
            c => c.ClientId == command.ClientId && !c.IsDeleted,
            cancellationToken);

        if (exists)
        {
            return Result.Failure<CreateM2MClientResponse>(M2MClientErrors.ClientIdAlreadyTaken);
        }

        string plaintextSecret = SecretGenerator.Generate();
        string hashed = hasher.Hash(plaintextSecret);

        M2MClient client = M2MClient.Register(
            tenantId,
            command.ClientId,
            hashed,
            command.DisplayName,
            command.AllowedScopes);

        db.M2MClients.Add(client);
        await db.SaveChangesAsync(cancellationToken);

        // Mirror the domain entity into OpenIddict so the client can use the token endpoint.
        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = command.ClientId,
            ClientSecret = plaintextSecret,
            ClientType = OpenIddictConstants.ClientTypes.Confidential,
            DisplayName = command.DisplayName,
            Permissions =
            {
                OpenIddictConstants.Permissions.Endpoints.Token,
                OpenIddictConstants.Permissions.GrantTypes.ClientCredentials,
            },
        };

        foreach (string scope in command.AllowedScopes)
        {
            descriptor.Permissions.Add(OpenIddictConstants.Permissions.Prefixes.Scope + scope);
        }

        await openIddictApps.CreateAsync(descriptor, cancellationToken);

        return new CreateM2MClientResponse(client.Id, client.ClientId, plaintextSecret);
    }
}

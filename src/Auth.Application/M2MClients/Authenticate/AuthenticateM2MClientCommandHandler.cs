using Auth.Application.Abstractions.Crypto;
using Auth.Application.Abstractions.Data;
using Auth.Application.Abstractions.Messaging;
using Auth.Domain.M2MClients;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Auth.Application.M2MClients.Authenticate;

public sealed class AuthenticateM2MClientCommandHandler(IAuthDbContext db, IClientSecretHasher hasher)
    : ICommandHandler<AuthenticateM2MClientCommand, M2MClientAuthenticated>
{
    public async Task<Result<M2MClientAuthenticated>> Handle(
        AuthenticateM2MClientCommand command,
        CancellationToken cancellationToken)
    {
        M2MClient? client = await db.M2MClients
            .FirstOrDefaultAsync(c => c.ClientId == command.ClientId, cancellationToken);

        if (client is null)
        {
            return Result.Failure<M2MClientAuthenticated>(M2MClientErrors.NotFound(command.ClientId));
        }

        if (!client.IsActive)
        {
            return Result.Failure<M2MClientAuthenticated>(M2MClientErrors.Inactive);
        }

        if (!hasher.Verify(command.ClientSecret, client.ClientSecretHash))
        {
            return Result.Failure<M2MClientAuthenticated>(M2MClientErrors.InvalidSecret);
        }

        return new M2MClientAuthenticated(client.TenantId, client.ClientId, [.. client.AllowedScopes]);
    }
}

using Auth.Application.Abstractions.Data;
using Auth.Application.Abstractions.Messaging;
using Auth.Application.Abstractions.Tenancy;
using Auth.Domain.Users;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Auth.Application.NetSuite.InitiateSso;

public sealed class InitiateNetSuiteSsoCommandHandler(
    IAuthDbContext db,
    INetSuiteSamlSigner signer,
    ITenantContext tenantContext)
    : ICommandHandler<InitiateNetSuiteSsoCommand, SignedNetSuiteAssertion>
{
    public async Task<Result<SignedNetSuiteAssertion>> Handle(
        InitiateNetSuiteSsoCommand command,
        CancellationToken cancellationToken)
    {
        Guid tenantId = tenantContext.TenantId;

        User? user = await db.Users
            .FirstOrDefaultAsync(
                u => u.Id == command.UserId && u.TenantId == tenantId,
                cancellationToken);

        if (user is null)
        {
            return Result.Failure<SignedNetSuiteAssertion>(UserErrors.NotFound(command.UserId));
        }

        if (!user.IsActive)
        {
            return Result.Failure<SignedNetSuiteAssertion>(UserErrors.Disabled);
        }

        if (string.IsNullOrWhiteSpace(user.NetSuiteEmail))
        {
            return Result.Failure<SignedNetSuiteAssertion>(UserErrors.NetSuiteEmailMissing);
        }

        SignedNetSuiteAssertion assertion = signer.Sign(
            user.NetSuiteEmail,
            user.Id,
            command.RelayState);

        return assertion;
    }
}

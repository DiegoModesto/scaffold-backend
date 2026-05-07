using Auth.Application.Abstractions.Identity;
using Auth.Application.Abstractions.Messaging;
using SharedKernel;

namespace Auth.Application.Users.GetEffectivePermissions;

public sealed class GetEffectivePermissionsQueryHandler(IPermissionResolver resolver)
    : IQueryHandler<GetEffectivePermissionsQuery, IReadOnlyCollection<string>>
{
    public Task<Result<IReadOnlyCollection<string>>> Handle(
        GetEffectivePermissionsQuery query,
        CancellationToken cancellationToken) =>
        resolver.ResolveAsync(query.TenantId, query.UserId, cancellationToken);
}

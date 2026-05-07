using Auth.Application.Abstractions.Messaging;

namespace Auth.Application.Users.GetEffectivePermissions;

public sealed record GetEffectivePermissionsQuery(Guid TenantId, Guid UserId)
    : IQuery<IReadOnlyCollection<string>>;

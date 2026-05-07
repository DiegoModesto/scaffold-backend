using Auth.Application.Abstractions.Messaging;

namespace Auth.Application.Users.SyncEntra;

public sealed record SyncEntraUserCommand(
    Guid TenantId,
    Guid EntraOid,
    string Email,
    string DisplayName) : ICommand<Guid>;

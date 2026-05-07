using Auth.Application.Abstractions.Messaging;

namespace Auth.Application.M2MClients.Authenticate;

public sealed record AuthenticateM2MClientCommand(string ClientId, string ClientSecret)
    : ICommand<M2MClientAuthenticated>;

public sealed record M2MClientAuthenticated(
    Guid TenantId,
    string ClientId,
    IReadOnlyCollection<string> AllowedScopes);

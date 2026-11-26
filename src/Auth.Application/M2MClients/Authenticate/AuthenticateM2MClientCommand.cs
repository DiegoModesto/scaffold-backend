using Auth.Application.Abstractions.Messaging;
using Auth.Application.M2MClients.Dtos;

namespace Auth.Application.M2MClients.Authenticate;

public sealed record AuthenticateM2MClientCommand(string ClientId, string ClientSecret)
    : ICommand<M2MClientAuthenticated>;

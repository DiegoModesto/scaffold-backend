using Auth.Application.Abstractions.Messaging;
using Auth.Application.Admin.M2MClients.Dtos;

namespace Auth.Application.Admin.M2MClients.Create;

public sealed record CreateM2MClientCommand(
    string ClientId,
    string DisplayName,
    IReadOnlyCollection<string> AllowedScopes) : ICommand<CreateM2MClientResponse>;

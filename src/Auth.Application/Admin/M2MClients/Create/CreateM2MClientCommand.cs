using Auth.Application.Abstractions.Messaging;

namespace Auth.Application.Admin.M2MClients.Create;

public sealed record CreateM2MClientCommand(
    string ClientId,
    string DisplayName,
    IReadOnlyCollection<string> AllowedScopes) : ICommand<CreateM2MClientResponse>;

public sealed record CreateM2MClientResponse(Guid Id, string ClientId, string ClientSecret);

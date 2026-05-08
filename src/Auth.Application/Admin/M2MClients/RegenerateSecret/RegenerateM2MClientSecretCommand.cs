using Auth.Application.Abstractions.Messaging;

namespace Auth.Application.Admin.M2MClients.RegenerateSecret;

public sealed record RegenerateM2MClientSecretCommand(Guid Id) : ICommand<string>;

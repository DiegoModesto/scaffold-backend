using Auth.Application.Abstractions.Messaging;

namespace Auth.Application.Admin.M2MClients.Deactivate;

public sealed record DeactivateM2MClientCommand(Guid Id) : ICommand;

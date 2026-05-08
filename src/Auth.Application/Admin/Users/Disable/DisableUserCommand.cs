using Auth.Application.Abstractions.Messaging;

namespace Auth.Application.Admin.Users.Disable;

public sealed record DisableUserCommand(Guid Id) : ICommand;

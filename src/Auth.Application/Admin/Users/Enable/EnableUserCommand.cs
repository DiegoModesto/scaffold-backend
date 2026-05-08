using Auth.Application.Abstractions.Messaging;

namespace Auth.Application.Admin.Users.Enable;

public sealed record EnableUserCommand(Guid Id) : ICommand;

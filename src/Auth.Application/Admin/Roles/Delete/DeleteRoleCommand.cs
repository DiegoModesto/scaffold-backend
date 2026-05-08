using Auth.Application.Abstractions.Messaging;

namespace Auth.Application.Admin.Roles.Delete;

public sealed record DeleteRoleCommand(Guid Id) : ICommand;

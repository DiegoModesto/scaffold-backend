using Auth.Application.Abstractions.Messaging;

namespace Auth.Application.Admin.Roles.Update;

public sealed record UpdateRoleCommand(Guid Id, string Name, string Description) : ICommand;

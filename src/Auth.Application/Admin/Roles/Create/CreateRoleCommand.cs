using Auth.Application.Abstractions.Messaging;

namespace Auth.Application.Admin.Roles.Create;

public sealed record CreateRoleCommand(string Name, string Description) : ICommand<Guid>;

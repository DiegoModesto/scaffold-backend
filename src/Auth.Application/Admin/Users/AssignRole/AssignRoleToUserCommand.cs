using Auth.Application.Abstractions.Messaging;

namespace Auth.Application.Admin.Users.AssignRole;

public sealed record AssignRoleToUserCommand(Guid UserId, Guid RoleId) : ICommand;

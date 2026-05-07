using Auth.Application.Abstractions.Messaging;

namespace Auth.Application.Admin.Users.RevokeRole;

public sealed record RevokeRoleFromUserCommand(Guid UserId, Guid RoleId) : ICommand;

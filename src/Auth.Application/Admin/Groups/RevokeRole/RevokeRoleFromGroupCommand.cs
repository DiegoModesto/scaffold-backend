using Auth.Application.Abstractions.Messaging;

namespace Auth.Application.Admin.Groups.RevokeRole;

public sealed record RevokeRoleFromGroupCommand(Guid GroupId, Guid RoleId) : ICommand;

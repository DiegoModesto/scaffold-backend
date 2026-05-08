using Auth.Application.Abstractions.Messaging;

namespace Auth.Application.Admin.Groups.AssignRole;

public sealed record AssignRoleToGroupCommand(Guid GroupId, Guid RoleId) : ICommand;

using Auth.Application.Abstractions.Messaging;

namespace Auth.Application.Admin.Roles.AssignPermission;

public sealed record AssignPermissionToRoleCommand(Guid RoleId, Guid PermissionId) : ICommand;

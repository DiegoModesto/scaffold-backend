using Auth.Application.Abstractions.Messaging;

namespace Auth.Application.Admin.Roles.RevokePermission;

public sealed record RevokePermissionFromRoleCommand(Guid RoleId, Guid PermissionId) : ICommand;

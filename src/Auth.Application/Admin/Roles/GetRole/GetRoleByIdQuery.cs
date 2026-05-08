using Auth.Application.Abstractions.Messaging;

namespace Auth.Application.Admin.Roles.GetRole;

public sealed record GetRoleByIdQuery(Guid Id) : IQuery<RoleDetailResponse>;

public sealed record RolePermissionRef(Guid Id, string Code, string Description);

public sealed record RoleDetailResponse(
    Guid Id,
    string Name,
    string Description,
    IReadOnlyCollection<RolePermissionRef> AssignedPermissions);

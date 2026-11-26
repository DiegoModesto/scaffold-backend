namespace Auth.Application.Admin.Roles.Dtos;

public sealed record RoleDetailResponse(
    Guid Id,
    string Name,
    string Description,
    IReadOnlyCollection<RolePermissionRef> AssignedPermissions);

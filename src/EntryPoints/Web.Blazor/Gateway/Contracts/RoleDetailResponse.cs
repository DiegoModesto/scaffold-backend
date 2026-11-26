namespace Web.Blazor.Gateway.Contracts;

public sealed record RoleDetailResponse(
    Guid Id,
    string Name,
    string Description,
    IReadOnlyCollection<PermissionRef> AssignedPermissions);

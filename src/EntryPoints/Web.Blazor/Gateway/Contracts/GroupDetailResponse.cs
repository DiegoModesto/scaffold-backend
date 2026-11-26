namespace Web.Blazor.Gateway.Contracts;

public sealed record GroupDetailResponse(
    Guid Id,
    string Name,
    string Description,
    Guid? EntraGroupId,
    IReadOnlyCollection<RoleRef> AssignedRoles);

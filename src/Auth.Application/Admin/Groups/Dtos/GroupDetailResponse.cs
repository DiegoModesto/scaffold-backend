namespace Auth.Application.Admin.Groups.Dtos;

public sealed record GroupDetailResponse(
    Guid Id,
    string Name,
    string Description,
    Guid? EntraGroupId,
    IReadOnlyCollection<GroupRoleRef> AssignedRoles);

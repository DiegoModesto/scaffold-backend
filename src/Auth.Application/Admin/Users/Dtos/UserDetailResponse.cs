namespace Auth.Application.Admin.Users.Dtos;

public sealed record UserDetailResponse(
    Guid Id,
    string Email,
    string DisplayName,
    string? NetSuiteEmail,
    bool IsActive,
    bool IsPreProvisioned,
    Guid? EntraOid,
    DateTimeOffset? LastLoginAt,
    IReadOnlyCollection<RoleRef> AssignedRoles,
    IReadOnlyCollection<GroupRef> AssignedGroups);

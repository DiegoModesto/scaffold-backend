namespace Web.Blazor.Gateway.Contracts;

public sealed record UserDetailResponse(
    Guid Id,
    string Email,
    string DisplayName,
    string? NetSuiteEmail,
    bool IsActive,
    bool IsPreProvisioned,
    DateTimeOffset? LastLoginAt,
    IReadOnlyCollection<RoleRef> AssignedRoles,
    IReadOnlyCollection<GroupRef> AssignedGroups);

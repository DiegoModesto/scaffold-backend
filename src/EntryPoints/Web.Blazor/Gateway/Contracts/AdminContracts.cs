namespace Web.Blazor.Gateway.Contracts;

public sealed record PagedResponse<T>(
    IReadOnlyCollection<T> Items,
    int Page,
    int PageSize,
    int Total);

public sealed record UserSummary(
    Guid Id,
    string Email,
    string DisplayName,
    bool IsActive);

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

public sealed record RoleRef(Guid Id, string Name);

public sealed record GroupRef(Guid Id, string Name);

public sealed record GroupSummary(
    Guid Id,
    string Name,
    string Description,
    bool HasEntraLink);

public sealed record GroupDetailResponse(
    Guid Id,
    string Name,
    string Description,
    Guid? EntraGroupId,
    IReadOnlyCollection<RoleRef> AssignedRoles);

public sealed record RoleSummary(
    Guid Id,
    string Name,
    string Description);

public sealed record RoleDetailResponse(
    Guid Id,
    string Name,
    string Description,
    IReadOnlyCollection<PermissionRef> AssignedPermissions);

public sealed record PermissionRef(Guid Id, string Code);

public sealed record PermissionResponse(Guid Id, string Code, string Description);

public sealed record M2MClientSummary(
    Guid Id,
    string ClientId,
    string DisplayName,
    bool IsActive);

public sealed record M2MClientDetailResponse(
    Guid Id,
    string ClientId,
    string DisplayName,
    IReadOnlyCollection<string> AllowedScopes,
    bool IsActive);

public sealed record CreateM2MClientResponse(
    Guid Id,
    string ClientId,
    string ClientSecret);

public sealed record AuthAuditEventResponse(
    Guid Id,
    Guid? UserId,
    string EventType,
    string Ip,
    string UserAgent,
    string Detail,
    DateTimeOffset OccurredAt);

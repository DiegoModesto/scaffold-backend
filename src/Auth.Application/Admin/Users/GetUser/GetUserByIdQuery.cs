using Auth.Application.Abstractions.Messaging;

namespace Auth.Application.Admin.Users.GetUser;

public sealed record GetUserByIdQuery(Guid Id) : IQuery<UserDetailResponse>;

public sealed record RoleRef(Guid Id, string Name);

public sealed record GroupRef(Guid Id, string Name);

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

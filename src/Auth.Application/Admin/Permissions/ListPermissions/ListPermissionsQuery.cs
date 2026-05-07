using Auth.Application.Abstractions.Messaging;

namespace Auth.Application.Admin.Permissions.ListPermissions;

public sealed record ListPermissionsQuery() : IQuery<IReadOnlyCollection<PermissionResponse>>;

public sealed record PermissionResponse(Guid Id, string Code, string Description);

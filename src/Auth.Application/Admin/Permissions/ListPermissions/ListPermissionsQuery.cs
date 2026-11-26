using Auth.Application.Abstractions.Messaging;
using Auth.Application.Admin.Permissions.Dtos;

namespace Auth.Application.Admin.Permissions.ListPermissions;

public sealed record ListPermissionsQuery() : IQuery<IReadOnlyCollection<PermissionResponse>>;

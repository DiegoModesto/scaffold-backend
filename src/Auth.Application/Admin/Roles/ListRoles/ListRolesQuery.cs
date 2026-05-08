using Auth.Application.Abstractions.Messaging;
using Auth.Application.Common;

namespace Auth.Application.Admin.Roles.ListRoles;

public sealed record ListRolesQuery(int Page, int PageSize, string? Search)
    : IQuery<PagedResponse<RoleSummary>>;

public sealed record RoleSummary(Guid Id, string Name, string Description);

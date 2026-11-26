using Auth.Application.Abstractions.Messaging;
using Auth.Application.Admin.Roles.Dtos;
using Auth.Application.Common;
using Auth.Application.Common.Dtos;

namespace Auth.Application.Admin.Roles.ListRoles;

public sealed record ListRolesQuery(int Page, int PageSize, string? Search)
    : IQuery<PagedResponse<RoleSummary>>;

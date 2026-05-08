using Auth.Application.Abstractions.Messaging;
using Auth.Application.Common;

namespace Auth.Application.Admin.Users.ListUsers;

public sealed record ListUsersQuery(int Page, int PageSize, string? Search)
    : IQuery<PagedResponse<UserSummary>>;

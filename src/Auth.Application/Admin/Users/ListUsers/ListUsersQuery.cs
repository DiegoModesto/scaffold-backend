using Auth.Application.Abstractions.Messaging;
using Auth.Application.Admin.Users.Dtos;
using Auth.Application.Common;
using Auth.Application.Common.Dtos;

namespace Auth.Application.Admin.Users.ListUsers;

public sealed record ListUsersQuery(int Page, int PageSize, string? Search)
    : IQuery<PagedResponse<UserSummary>>;

using Auth.Application.Abstractions.Messaging;
using Auth.Application.Admin.Groups.Dtos;
using Auth.Application.Common;
using Auth.Application.Common.Dtos;

namespace Auth.Application.Admin.Groups.ListGroups;

public sealed record ListGroupsQuery(int Page, int PageSize, string? Search)
    : IQuery<PagedResponse<GroupSummary>>;

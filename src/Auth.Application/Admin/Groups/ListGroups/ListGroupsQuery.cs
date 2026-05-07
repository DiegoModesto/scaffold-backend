using Auth.Application.Abstractions.Messaging;
using Auth.Application.Common;

namespace Auth.Application.Admin.Groups.ListGroups;

public sealed record ListGroupsQuery(int Page, int PageSize, string? Search)
    : IQuery<PagedResponse<GroupSummary>>;

public sealed record GroupSummary(Guid Id, string Name, string Description, Guid? EntraGroupId);

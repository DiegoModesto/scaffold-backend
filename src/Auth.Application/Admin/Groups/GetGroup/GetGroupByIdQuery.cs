using Auth.Application.Abstractions.Messaging;

namespace Auth.Application.Admin.Groups.GetGroup;

public sealed record GetGroupByIdQuery(Guid Id) : IQuery<GroupDetailResponse>;

public sealed record GroupRoleRef(Guid Id, string Name);

public sealed record GroupDetailResponse(
    Guid Id,
    string Name,
    string Description,
    Guid? EntraGroupId,
    IReadOnlyCollection<GroupRoleRef> AssignedRoles);

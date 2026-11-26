using Auth.Application.Abstractions.Messaging;
using Auth.Application.Admin.Groups.Dtos;

namespace Auth.Application.Admin.Groups.GetGroup;

public sealed record GetGroupByIdQuery(Guid Id) : IQuery<GroupDetailResponse>;

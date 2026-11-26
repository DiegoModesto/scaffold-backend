using Auth.Application.Abstractions.Messaging;
using Auth.Application.Admin.Roles.Dtos;

namespace Auth.Application.Admin.Roles.GetRole;

public sealed record GetRoleByIdQuery(Guid Id) : IQuery<RoleDetailResponse>;

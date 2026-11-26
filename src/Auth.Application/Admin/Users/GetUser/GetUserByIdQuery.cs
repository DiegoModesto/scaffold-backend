using Auth.Application.Abstractions.Messaging;
using Auth.Application.Admin.Users.Dtos;

namespace Auth.Application.Admin.Users.GetUser;

public sealed record GetUserByIdQuery(Guid Id) : IQuery<UserDetailResponse>;

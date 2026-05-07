using Auth.Application.Abstractions.Messaging;

namespace Auth.Application.Admin.Users.RemoveFromGroup;

public sealed record RemoveUserFromGroupCommand(Guid UserId, Guid GroupId) : ICommand;

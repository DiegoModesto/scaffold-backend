using Auth.Application.Abstractions.Messaging;

namespace Auth.Application.Admin.Users.AddToGroup;

public sealed record AddUserToGroupCommand(Guid UserId, Guid GroupId) : ICommand;

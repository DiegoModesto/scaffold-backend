using Auth.Application.Abstractions.Messaging;

namespace Auth.Application.Admin.Groups.Update;

public sealed record UpdateGroupCommand(Guid Id, string Name, string Description, Guid? EntraGroupId)
    : ICommand;

using Auth.Application.Abstractions.Messaging;

namespace Auth.Application.Admin.Groups.Create;

public sealed record CreateGroupCommand(string Name, string Description, Guid? EntraGroupId)
    : ICommand<Guid>;

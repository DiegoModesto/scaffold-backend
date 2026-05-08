using Auth.Application.Abstractions.Messaging;

namespace Auth.Application.Admin.Groups.Delete;

public sealed record DeleteGroupCommand(Guid Id) : ICommand;

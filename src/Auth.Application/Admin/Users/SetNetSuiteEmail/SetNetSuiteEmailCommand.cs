using Auth.Application.Abstractions.Messaging;

namespace Auth.Application.Admin.Users.SetNetSuiteEmail;

public sealed record SetNetSuiteEmailCommand(Guid Id, string? NetSuiteEmail) : ICommand;

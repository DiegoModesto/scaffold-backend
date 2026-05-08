using Auth.Application.Abstractions.Messaging;

namespace Auth.Application.Admin.Users.PreProvision;

public sealed record PreProvisionUserCommand(
    string Email,
    string DisplayName,
    string? NetSuiteEmail) : ICommand<Guid>;

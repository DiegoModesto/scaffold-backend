using Auth.Application.Abstractions.Messaging;

namespace Auth.Application.NetSuite.InitiateSso;

public sealed record InitiateNetSuiteSsoCommand(Guid UserId, string? RelayState)
    : ICommand<SignedNetSuiteAssertion>;

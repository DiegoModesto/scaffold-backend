using Auth.Application.Abstractions.Messaging;
using Auth.Application.NetSuite.Dtos;

namespace Auth.Application.NetSuite.InitiateSso;

public sealed record InitiateNetSuiteSsoCommand(Guid UserId, string? RelayState)
    : ICommand<SignedNetSuiteAssertion>;

namespace Infra.Authentication;

public sealed class InvalidClaimException(string message) : Exception(message);

namespace Application.Abstractions.Authentication;

public sealed record TokenInfo(string AccessToken, DateTimeOffset ExpiresAt);

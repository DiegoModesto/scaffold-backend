namespace Application.Abstractions.Authentication.Dtos;

public sealed record TokenInfo(string AccessToken, DateTimeOffset ExpiresAt);

namespace Web.Blazor.Authentication.TokenStore.Dtos;

public sealed record SessionTokens(
    string AccessToken,
    string? RefreshToken,
    string? IdToken,
    DateTimeOffset ExpiresAt);

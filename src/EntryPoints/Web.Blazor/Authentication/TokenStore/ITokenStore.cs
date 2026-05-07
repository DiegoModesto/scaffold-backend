namespace Web.Blazor.Authentication.TokenStore;

public sealed record SessionTokens(
    string AccessToken,
    string? RefreshToken,
    string? IdToken,
    DateTimeOffset ExpiresAt);

public interface ITokenStore
{
    Task SaveAsync(string sessionId, SessionTokens tokens, CancellationToken cancellationToken = default);

    Task<SessionTokens?> GetAsync(string sessionId, CancellationToken cancellationToken = default);

    Task RemoveAsync(string sessionId, CancellationToken cancellationToken = default);
}

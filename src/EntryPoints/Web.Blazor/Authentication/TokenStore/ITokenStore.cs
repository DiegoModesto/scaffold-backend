using Web.Blazor.Authentication.TokenStore.Dtos;

namespace Web.Blazor.Authentication.TokenStore;

public interface ITokenStore
{
    Task SaveAsync(string sessionId, SessionTokens tokens, CancellationToken cancellationToken = default);

    Task<SessionTokens?> GetAsync(string sessionId, CancellationToken cancellationToken = default);

    Task RemoveAsync(string sessionId, CancellationToken cancellationToken = default);
}

namespace Web.Blazor.Gateway.Contracts;

public sealed record M2MClientDetailResponse(
    Guid Id,
    string ClientId,
    string DisplayName,
    IReadOnlyCollection<string> AllowedScopes,
    bool IsActive);

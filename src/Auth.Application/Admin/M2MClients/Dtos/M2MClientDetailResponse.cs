namespace Auth.Application.Admin.M2MClients.Dtos;

public sealed record M2MClientDetailResponse(
    Guid Id,
    string ClientId,
    string DisplayName,
    bool IsActive,
    IReadOnlyCollection<string> AllowedScopes);

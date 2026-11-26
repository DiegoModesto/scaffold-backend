namespace Auth.Application.M2MClients.Dtos;

public sealed record M2MClientAuthenticated(
    Guid TenantId,
    string ClientId,
    IReadOnlyCollection<string> AllowedScopes);

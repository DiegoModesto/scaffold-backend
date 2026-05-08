using Auth.Application.Abstractions.Messaging;

namespace Auth.Application.Admin.M2MClients.GetM2MClient;

public sealed record GetM2MClientByIdQuery(Guid Id) : IQuery<M2MClientDetailResponse>;

public sealed record M2MClientDetailResponse(
    Guid Id,
    string ClientId,
    string DisplayName,
    bool IsActive,
    IReadOnlyCollection<string> AllowedScopes);

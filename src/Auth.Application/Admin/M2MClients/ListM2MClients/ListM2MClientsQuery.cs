using Auth.Application.Abstractions.Messaging;
using Auth.Application.Common;

namespace Auth.Application.Admin.M2MClients.ListM2MClients;

public sealed record ListM2MClientsQuery(int Page, int PageSize, string? Search)
    : IQuery<PagedResponse<M2MClientSummary>>;

public sealed record M2MClientSummary(
    Guid Id,
    string ClientId,
    string DisplayName,
    bool IsActive,
    IReadOnlyCollection<string> AllowedScopes);

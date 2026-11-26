using Auth.Application.Abstractions.Messaging;
using Auth.Application.Admin.M2MClients.Dtos;
using Auth.Application.Common;
using Auth.Application.Common.Dtos;

namespace Auth.Application.Admin.M2MClients.ListM2MClients;

public sealed record ListM2MClientsQuery(int Page, int PageSize, string? Search)
    : IQuery<PagedResponse<M2MClientSummary>>;

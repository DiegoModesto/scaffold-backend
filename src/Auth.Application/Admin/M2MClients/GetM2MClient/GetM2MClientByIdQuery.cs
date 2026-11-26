using Auth.Application.Abstractions.Messaging;
using Auth.Application.Admin.M2MClients.Dtos;

namespace Auth.Application.Admin.M2MClients.GetM2MClient;

public sealed record GetM2MClientByIdQuery(Guid Id) : IQuery<M2MClientDetailResponse>;

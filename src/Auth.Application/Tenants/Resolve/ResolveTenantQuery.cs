using Auth.Application.Abstractions.Messaging;
using Auth.Application.Tenants.Dtos;

namespace Auth.Application.Tenants.Resolve;

public sealed record ResolveTenantQuery(Guid EntraTenantId) : IQuery<ResolveTenantResponse>;

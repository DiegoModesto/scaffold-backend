using Auth.Application.Abstractions.Messaging;
using Auth.Domain.Tenants;

namespace Auth.Application.Tenants.Resolve;

public sealed record ResolveTenantQuery(Guid EntraTenantId) : IQuery<Tenant>;

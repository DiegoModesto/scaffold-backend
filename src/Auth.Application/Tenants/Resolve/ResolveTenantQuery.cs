using Auth.Application.Abstractions.Messaging;

namespace Auth.Application.Tenants.Resolve;

public sealed record ResolveTenantResponse(Guid Id, Guid EntraTenantId, bool IsActive);

public sealed record ResolveTenantQuery(Guid EntraTenantId) : IQuery<ResolveTenantResponse>;

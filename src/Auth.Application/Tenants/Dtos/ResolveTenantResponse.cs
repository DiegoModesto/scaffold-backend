namespace Auth.Application.Tenants.Dtos;

public sealed record ResolveTenantResponse(Guid Id, Guid EntraTenantId, bool IsActive);

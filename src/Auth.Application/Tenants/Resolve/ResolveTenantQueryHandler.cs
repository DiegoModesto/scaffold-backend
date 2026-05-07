using Auth.Application.Abstractions.Data;
using Auth.Application.Abstractions.Messaging;
using Auth.Domain.Tenants;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Auth.Application.Tenants.Resolve;

public sealed class ResolveTenantQueryHandler(IAuthDbContext db)
    : IQueryHandler<ResolveTenantQuery, ResolveTenantResponse>
{
    public async Task<Result<ResolveTenantResponse>> Handle(ResolveTenantQuery query, CancellationToken cancellationToken)
    {
        Tenant? tenant = await db.Tenants
            .FirstOrDefaultAsync(t => t.EntraTenantId == query.EntraTenantId, cancellationToken);

        if (tenant is null)
        {
            return Result.Failure<ResolveTenantResponse>(TenantErrors.NotRegistered(query.EntraTenantId));
        }

        if (!tenant.IsActive)
        {
            return Result.Failure<ResolveTenantResponse>(TenantErrors.Inactive);
        }

        return new ResolveTenantResponse(tenant.Id, tenant.EntraTenantId, tenant.IsActive);
    }
}

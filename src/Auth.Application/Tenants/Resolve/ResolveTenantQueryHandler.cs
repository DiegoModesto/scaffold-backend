using Auth.Application.Abstractions.Data;
using Auth.Application.Abstractions.Messaging;
using Auth.Domain.Tenants;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Auth.Application.Tenants.Resolve;

public sealed class ResolveTenantQueryHandler(IAuthDbContext db)
    : IQueryHandler<ResolveTenantQuery, Tenant>
{
    public async Task<Result<Tenant>> Handle(ResolveTenantQuery query, CancellationToken cancellationToken)
    {
        Tenant? tenant = await db.Tenants
            .FirstOrDefaultAsync(t => t.EntraTenantId == query.EntraTenantId, cancellationToken);

        if (tenant is null)
        {
            return Result.Failure<Tenant>(TenantErrors.NotRegistered(query.EntraTenantId));
        }

        if (!tenant.IsActive)
        {
            return Result.Failure<Tenant>(TenantErrors.Inactive);
        }

        return tenant;
    }
}

using Auth.Application.Abstractions.Data;
using Auth.Application.Abstractions.Messaging;
using Auth.Application.Abstractions.Tenancy;
using Auth.Domain.M2MClients;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Auth.Application.Admin.M2MClients.GetM2MClient;

public sealed class GetM2MClientByIdQueryHandler(IAuthDbContext db, ITenantContext tenant)
    : IQueryHandler<GetM2MClientByIdQuery, M2MClientDetailResponse>
{
    public async Task<Result<M2MClientDetailResponse>> Handle(
        GetM2MClientByIdQuery query,
        CancellationToken cancellationToken)
    {
        Guid tenantId = tenant.TenantId;
        M2MClient? client = await db.M2MClients.FirstOrDefaultAsync(
            c => c.Id == query.Id && c.TenantId == tenantId && !c.IsDeleted,
            cancellationToken);

        if (client is null)
        {
            return Result.Failure<M2MClientDetailResponse>(M2MClientErrors.NotFound(query.Id));
        }

        return new M2MClientDetailResponse(
            client.Id,
            client.ClientId,
            client.DisplayName,
            client.IsActive,
            [.. client.AllowedScopes]);
    }
}

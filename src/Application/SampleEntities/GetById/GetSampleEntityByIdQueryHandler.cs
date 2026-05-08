using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.SampleEntities;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.SampleEntities.GetById;

public sealed class GetSampleEntityByIdQueryHandler(
    IApplicationDbContext dbContext,
    IUserContext userContext)
    : IQueryHandler<GetSampleEntityByIdQuery, SampleEntityResponse>
{
    public async Task<Result<SampleEntityResponse>> Handle(
        GetSampleEntityByIdQuery query,
        CancellationToken cancellationToken)
    {
        Guid? tenantId = userContext.TenantId;

        SampleEntityResponse? response = await dbContext.SampleEntities
            .Where(e => e.Id == query.Id
                && !e.IsDeleted
                && (tenantId == null || e.TenantId == tenantId))
            .Select(e => new SampleEntityResponse(e.Id, e.Name, e.Description))
            .FirstOrDefaultAsync(cancellationToken);

        return response is null
            ? Result.Failure<SampleEntityResponse>(SampleEntityErrors.NotFound(query.Id))
            : response;
    }
}

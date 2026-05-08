using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.SampleEntities;
using SharedKernel;

namespace Application.SampleEntities.Create;

public sealed class CreateSampleEntityCommandHandler(
    IApplicationDbContext dbContext,
    IUserContext userContext)
    : ICommandHandler<CreateSampleEntityCommand, Guid>
{
    public async Task<Result<Guid>> Handle(
        CreateSampleEntityCommand command,
        CancellationToken cancellationToken)
    {
        Guid tenantId = userContext.TenantId
            ?? throw new InvalidOperationException("TenantId is required to create a SampleEntity.");

        var entity = new SampleEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = command.Name,
            Description = command.Description,
            CreatedAt = DateTimeOffset.UtcNow
        };

        dbContext.SampleEntities.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);

        return entity.Id;
    }
}

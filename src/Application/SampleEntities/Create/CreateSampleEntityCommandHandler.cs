using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.SampleEntities;
using SharedKernel;

namespace Application.SampleEntities.Create;

public sealed class CreateSampleEntityCommandHandler(IApplicationDbContext dbContext)
    : ICommandHandler<CreateSampleEntityCommand, Guid>
{
    public async Task<Result<Guid>> Handle(
        CreateSampleEntityCommand command,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Name))
        {
            return Result.Failure<Guid>(SampleEntityErrors.NameRequired);
        }

        var entity = new SampleEntity
        {
            Id = Guid.NewGuid(),
            Name = command.Name,
            Description = command.Description,
            CreatedAt = DateTimeOffset.UtcNow
        };

        dbContext.SampleEntities.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);

        return entity.Id;
    }
}

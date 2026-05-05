using Application.Abstractions.Messaging;
using Application.SampleEntities.Events;
using SharedKernel;

namespace Application.SampleEntities.Publish;

public sealed class PublishSampleEventCommandHandler(IMessagePublisher publisher)
    : ICommandHandler<PublishSampleEventCommand, Guid>
{
    public const string RoutingKey = "sample.created";

    public async Task<Result<Guid>> Handle(
        PublishSampleEventCommand command,
        CancellationToken cancellationToken)
    {
        var @event = new SampleEntityCreatedEvent(
            Id: Guid.NewGuid(),
            Name: command.Name,
            Description: command.Description,
            OccurredAt: DateTimeOffset.UtcNow);

        await publisher.PublishAsync(@event, RoutingKey, cancellationToken);

        return @event.Id;
    }
}

using Application.Abstractions.Messaging;
using Application.SampleEntities.Events;
using Application.SampleEntities.Publish;
using Microsoft.Extensions.Options;

namespace CronJobs.Jobs;

public sealed class SamplePollingJob(
    IMessagePublisher publisher,
    IOptions<CronJobsOptions> options,
    ILogger<SamplePollingJob> logger)
    : CronBackgroundService(options.Value.SamplePollingSchedule, logger)
{
    protected override async Task RunAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("SamplePollingJob polling at {Now:O}", DateTimeOffset.UtcNow);

        // Replace this with the actual external source you want to poll (HTTP API, file system, DB, etc.).
        var @event = new SampleEntityCreatedEvent(
            Id: Guid.NewGuid(),
            Name: $"polled-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}",
            Description: "Synthetic event produced by SamplePollingJob",
            OccurredAt: DateTimeOffset.UtcNow);

        await publisher.PublishAsync(@event, PublishSampleEventCommandHandler.RoutingKey, cancellationToken);
    }
}

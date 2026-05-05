using Application.Abstractions.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CronJobs.Jobs;

public sealed class SampleCronJob(
    IServiceScopeFactory scopeFactory,
    IOptions<CronJobsOptions> options,
    ILogger<SampleCronJob> logger)
    : CronBackgroundService(options.Value.SampleSchedule, logger)
{
    protected override async Task RunAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("SampleCronJob firing at {Now:O}", DateTimeOffset.UtcNow);

        using IServiceScope scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        int total = await dbContext.SampleEntities.CountAsync(cancellationToken);
        logger.LogInformation("Found {Total} sample entities", total);
    }
}

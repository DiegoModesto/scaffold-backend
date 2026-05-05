using Cronos;

namespace CronJobs.Jobs;

public abstract class CronBackgroundService(string cronExpression, ILogger logger) : BackgroundService
{
    private readonly CronExpression _cron = CronExpression.Parse(cronExpression, CronFormat.Standard);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            DateTimeOffset? next = _cron.GetNextOccurrence(DateTimeOffset.UtcNow, TimeZoneInfo.Utc);
            if (next is null)
            {
                logger.LogWarning("No next occurrence for cron expression '{Cron}'. Stopping job.", cronExpression);
                return;
            }

            TimeSpan delay = next.Value - DateTimeOffset.UtcNow;
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, stoppingToken);
            }

            try
            {
                await RunAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unhandled exception executing cron job {Job}", GetType().Name);
            }
        }
    }

    protected abstract Task RunAsync(CancellationToken cancellationToken);
}

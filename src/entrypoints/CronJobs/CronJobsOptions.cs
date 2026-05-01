namespace CronJobs;

public sealed class CronJobsOptions
{
    public string SampleSchedule { get; set; } = "*/5 * * * *";
    public string SamplePollingSchedule { get; set; } = "*/2 * * * *";
}

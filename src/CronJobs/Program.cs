using CronJobs;
using CronJobs.Jobs;
using Infra;
using Serilog;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSerilog((services, loggerConfig) =>
    loggerConfig.ReadFrom.Configuration(builder.Configuration));

builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.Configure<CronJobsOptions>(builder.Configuration.GetSection("CronJobs"));

builder.Services.AddHostedService<SampleCronJob>();

IHost host = builder.Build();
await host.RunAsync();

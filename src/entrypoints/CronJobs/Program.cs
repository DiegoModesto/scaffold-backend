using CronJobs;
using CronJobs.Jobs;
using Infra;
using Infra.Extensions;
using Serilog;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.Configuration.MapEnvironmentVariables();

builder.Services.AddSerilog((services, loggerConfig) =>
    loggerConfig.ReadFrom.Configuration(builder.Configuration));

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddInfrastructureMessaging(builder.Configuration);

builder.Services.Configure<CronJobsOptions>(builder.Configuration.GetSection("CronJobs"));

builder.Services.AddHostedService<SampleCronJob>();
builder.Services.AddHostedService<SamplePollingJob>();

IHost host = builder.Build();
await host.RunAsync();

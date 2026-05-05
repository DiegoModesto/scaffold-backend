using Infra;
using Infra.Extensions;
using Infra.Observability;
using Serilog;
using Worker.Messaging;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.Configuration.MapEnvironmentVariables();

builder.Services.AddSerilog((services, loggerConfig) =>
    loggerConfig.ReadFrom.Configuration(builder.Configuration));

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddInfrastructureMessaging(builder.Configuration);
builder.Services.AddOpenTelemetryObservability(builder.Configuration, serviceName: "Worker");

builder.Services.AddHostedService<SampleMessageConsumer>();

IHost host = builder.Build();
await host.RunAsync();

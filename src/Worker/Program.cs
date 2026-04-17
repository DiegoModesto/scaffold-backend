using Infra;
using Serilog;
using Worker;
using Worker.Messaging;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSerilog((services, loggerConfig) =>
    loggerConfig.ReadFrom.Configuration(builder.Configuration));

builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.Configure<RabbitMqOptions>(builder.Configuration.GetSection("RabbitMq"));
builder.Services.AddSingleton<RabbitMqConnectionFactory>();

builder.Services.AddHostedService<SampleMessageConsumer>();

IHost host = builder.Build();
await host.RunAsync();

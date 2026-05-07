using Infra.Observability;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Serilog;
using StackExchange.Redis;
using Web.Blazor.Authentication;
using Web.Blazor.Authentication.TokenStore;
using Web.Blazor.Components;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, loggerConfig) =>
    loggerConfig.ReadFrom.Configuration(context.Configuration));

builder.Services
    .AddOpenTelemetryObservability(builder.Configuration, serviceName: "Web.Blazor", includeAspNetCore: true);

string redisConnection = builder.Configuration["Redis:ConnectionString"]
    ?? throw new InvalidOperationException("Redis:ConnectionString must be configured.");

IConnectionMultiplexer redisMultiplexer = await ConnectionMultiplexer.ConnectAsync(redisConnection);
builder.Services.AddSingleton(redisMultiplexer);

builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = redisConnection;
    options.InstanceName = "bff:cache:";
});

builder.Services
    .AddDataProtection()
    .PersistKeysToStackExchangeRedis(redisMultiplexer, "bff:dp-keys")
    .SetApplicationName("Web.Blazor.BFF");

builder.Services.AddSingleton<ITokenStore, RedisTokenStore>();

builder.Services.AddBffAuthentication(builder.Configuration);
builder.Services.AddAuthorization();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

WebApplication app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAntiforgery();

app.UseSerilogRequestLogging();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/login", () => Results.Challenge(
    new AuthenticationProperties { RedirectUri = "/" },
    [BffAuthenticationExtensions.OidcScheme]));

app.MapPost("/logout", async (HttpContext http) =>
{
    await http.SignOutAsync(BffAuthenticationExtensions.CookieScheme);
    return Results.SignOut(
        new AuthenticationProperties { RedirectUri = "/" },
        [BffAuthenticationExtensions.OidcScheme]);
}).RequireAuthorization();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

await app.RunAsync();

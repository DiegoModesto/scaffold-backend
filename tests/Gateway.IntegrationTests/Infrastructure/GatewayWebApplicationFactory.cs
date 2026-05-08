using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;
using Testcontainers.Redis;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace Gateway.IntegrationTests.Infrastructure;

/// <summary>
/// Boots the Gateway behind a real Redis container plus two WireMock servers:
/// one impersonating Auth.API (discovery + introspection + health) and another
/// acting as a generic downstream backend so we can verify forwarded headers.
/// </summary>
public sealed class GatewayWebApplicationFactory
    : WebApplicationFactory<global::Gateway.Program>, IAsyncLifetime
{
    private readonly RedisContainer _redis = new RedisBuilder()
        .WithImage("redis:7-alpine")
        .Build();

    private WireMockServer _authApi = null!;
    private WireMockServer _webApi = null!;
    private IConnectionMultiplexer? _redisMultiplexer;

    public WireMockServer AuthApi => _authApi;
    public WireMockServer WebApi => _webApi;

    public async Task InitializeAsync()
    {
        await _redis.StartAsync();

        // Pre-warm: Testcontainers reports the container as started before the redis-server
        // accept loop is reliably listening. AspNetCore.HealthChecks.Redis 9.0.0 forces
        // AbortOnConnectFail=false and caches the resulting (possibly half-connected)
        // multiplexer, so a flaky initial connect leaves the gateway permanently unhealthy.
        // Spin until we get a successful PING with a small timeout.
        _redisMultiplexer = await WaitForRedisAsync(_redis.GetConnectionString());

        _authApi = WireMockServer.Start();
        _webApi = WireMockServer.Start();

        StubAuthHealth();
        StubAuthDiscovery();

        // Mirror config into env vars so AddGatewayAuthentication, which reads configuration
        // synchronously during host construction, sees the WireMock URLs rather than the
        // localhost:5100 defaults from appsettings.Development.json. Environment variables
        // beat appsettings*.json in ASP.NET Core's default configuration order.
        Environment.SetEnvironmentVariable("Redis__ConnectionString",
            $"{_redis.GetConnectionString()},abortConnect=false,connectRetry=5,connectTimeout=10000");
        Environment.SetEnvironmentVariable("Auth__Authority", _authApi.Url);
        Environment.SetEnvironmentVariable("Auth__IntrospectionEndpoint",
            $"{_authApi.Url}/connect/introspect");
        Environment.SetEnvironmentVariable("Auth__IntrospectionClientId", "gateway");
        Environment.SetEnvironmentVariable("Auth__IntrospectionClientSecret", "test-secret");
        Environment.SetEnvironmentVariable("IntrospectionCache__TtlSeconds", "30");
    }

    public new async Task DisposeAsync()
    {
        _authApi.Stop();
        _authApi.Dispose();
        _webApi.Stop();
        _webApi.Dispose();
        if (_redisMultiplexer is not null)
        {
            await _redisMultiplexer.DisposeAsync();
        }
        await _redis.DisposeAsync();
        await base.DisposeAsync();
    }

    Task IAsyncLifetime.DisposeAsync() => DisposeAsync();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        // Use UseSetting (sets in-memory before appsettings*.json load) AND a final
        // AddInMemoryCollection callback so values win over appsettings.Development.json.
        builder.ConfigureAppConfiguration((_, cfg) =>
        {
            cfg.Sources.Insert(cfg.Sources.Count, new Microsoft.Extensions.Configuration.Memory.MemoryConfigurationSource
            {
                InitialData = new Dictionary<string, string?>
                {
                    ["Redis:ConnectionString"] =
                        $"{_redis.GetConnectionString()},abortConnect=false,connectRetry=5,connectTimeout=10000",
                    ["Auth:Authority"] = _authApi.Url!,
                    ["Auth:IntrospectionEndpoint"] = $"{_authApi.Url}/connect/introspect",
                    ["Auth:IntrospectionClientId"] = "gateway",
                    ["Auth:IntrospectionClientSecret"] = "test-secret",
                    ["IntrospectionCache:TtlSeconds"] = "30",
                    ["ReverseProxy:Routes:test:ClusterId"] = "test-cluster",
                    ["ReverseProxy:Routes:test:Match:Path"] = "/api/test/{**catch-all}",
                    ["ReverseProxy:Routes:test:AuthorizationPolicy"] = "RequireBearer",
                    ["ReverseProxy:Routes:test:Transforms:0:PathRemovePrefix"] = "/api/test",
                    ["ReverseProxy:Clusters:test-cluster:Destinations:web:Address"] = $"{_webApi.Url}/",
                    // Suppress the appsettings.Development.json routes by making them empty
                    // strings — YARP ignores routes whose ClusterId is empty/missing.
                    ["ReverseProxy:Routes:auth-discovery:ClusterId"] = "",
                    ["ReverseProxy:Routes:auth-connect:ClusterId"] = "",
                    ["ReverseProxy:Routes:auth-admin:ClusterId"] = "",
                    ["ReverseProxy:Routes:web-api:ClusterId"] = "",
                    ["Logging:LogLevel:Default"] = "Warning",
                }
            });
        });


        builder.ConfigureTestServices(services =>
        {
            // Replace the AspNetCore.HealthChecks.Redis 9.0.0 registration. That package
            // forces AbortOnConnectFail=false and caches the resulting multiplexer; with
            // a brand-new Testcontainers Redis, the first ConnectAsync often returns a
            // half-connected multiplexer that then fails PingAsync ("Error connecting
            // right now"). For tests we replace the redis check with one driven by a
            // pre-warmed IConnectionMultiplexer that is guaranteed to be connected.
            HealthCheckServiceOptions options = services
                .BuildServiceProvider()
                .GetRequiredService<Microsoft.Extensions.Options.IOptions<HealthCheckServiceOptions>>()
                .Value;

            services.PostConfigure<HealthCheckServiceOptions>(opt =>
            {
                opt.Registrations.Clear();
                foreach (HealthCheckRegistration reg in options.Registrations
                    .Where(r => r.Name != "redis"))
                {
                    opt.Registrations.Add(reg);
                }

                opt.Registrations.Add(new HealthCheckRegistration(
                    "redis",
                    sp => new TestRedisHealthCheck(sp.GetRequiredService<IConnectionMultiplexer>()),
                    failureStatus: null,
                    tags: ["ready"]));
            });

            // Singleton multiplexer pre-warmed in InitializeAsync — wait synchronously
            // here for the pre-warm completion future before resolving.
            services.AddSingleton<IConnectionMultiplexer>(_ => _redisMultiplexer
                ?? throw new InvalidOperationException("Redis multiplexer not initialised."));
        });
    }

    private sealed class TestRedisHealthCheck(IConnectionMultiplexer mux) : IHealthCheck
    {
        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                await mux.GetDatabase().PingAsync();
                return HealthCheckResult.Healthy();
            }
            catch (Exception ex)
            {
                return HealthCheckResult.Unhealthy("Redis ping failed.", ex);
            }
        }
    }

    /// <summary>
    /// Stub the introspection endpoint so the supplied <paramref name="token"/> resolves
    /// to either an active or inactive response. The active response includes the audiences
    /// the Gateway requires (<c>api:web</c>, <c>api:auth</c>) plus an issuer matching the
    /// WireMock authority so OpenIddict's validators are satisfied.
    /// </summary>
    public void StubIntrospection(
        string token,
        bool active,
        params (string claim, string value)[] extraClaims)
    {
        string extras = string.Concat(
            extraClaims.Select(c => $", \"{c.claim}\": \"{c.value}\""));

        string json = active
            ? $$"""
                {
                  "active": true,
                  "sub": "user-1",
                  "tenant_id": "{{Guid.NewGuid()}}",
                  "aud": ["api:web", "api:auth"],
                  "iss": "{{_authApi.Url}}/",
                  "token_usage": "access_token"{{extras}}
                }
                """
            : """{"active":false}""";

        _authApi.Given(Request.Create()
                .WithPath("/connect/introspect")
                .WithBody(b => b != null && b.Contains($"token={token}"))
                .UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(json));
    }

    private void StubAuthHealth()
    {
        _authApi.Given(Request.Create().WithPath("/health/live").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""{"status":"live"}"""));
    }

    private void StubAuthDiscovery()
    {
        // OpenIddict.Validation pulls /.well-known/openid-configuration from the issuer
        // configured via SetIssuer(...) before falling back to the explicitly-set
        // IntrospectionEndpoint. The response must advertise the introspection endpoint
        // and use an issuer that exactly matches the SetIssuer URL OpenIddict was given.
        // OpenIddict 6.x validates the discovery document and requires jwks_uri (the
        // metadata reader rejects metadata without it even for introspection-only flows).
        // We provide a real RSA public key so the JWKS document parses cleanly; the key
        // is not actually used because tokens are opaque and validated via introspection.
        using var rsa = System.Security.Cryptography.RSA.Create(2048);
        System.Security.Cryptography.RSAParameters parms = rsa.ExportParameters(false);
        string n = Microsoft.IdentityModel.Tokens.Base64UrlEncoder.Encode(parms.Modulus!);
        string e = Microsoft.IdentityModel.Tokens.Base64UrlEncoder.Encode(parms.Exponent!);

        var discovery = new
        {
            issuer = _authApi.Url,
            introspection_endpoint = $"{_authApi.Url}/connect/introspect",
            jwks_uri = $"{_authApi.Url}/.well-known/jwks",
            grant_types_supported = new[] { "client_credentials" },
            response_types_supported = new[] { "token" },
            token_endpoint_auth_methods_supported = new[]
            {
                "client_secret_basic", "client_secret_post"
            },
            introspection_endpoint_auth_methods_supported = new[]
            {
                "client_secret_basic", "client_secret_post"
            },
            id_token_signing_alg_values_supported = new[] { "RS256" },
            subject_types_supported = new[] { "public" },
        };

        var jwks = new
        {
            keys = new object[]
            {
                new { kty = "RSA", use = "sig", alg = "RS256", kid = "test-1", n, e },
            },
        };

        _authApi.Given(Request.Create()
                .WithPath("/.well-known/openid-configuration")
                .UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBodyAsJson(discovery));

        _authApi.Given(Request.Create()
                .WithPath("/.well-known/jwks")
                .UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBodyAsJson(jwks));
    }

    /// <summary>
    /// Stub the downstream backend's <c>/echo</c> with a static body. WireMock.Net 2.5.0
    /// does not expose <c>WithBodyFromTemplate</c>, so tests assert forwarded headers via
    /// the <see cref="WebApi"/> request log instead.
    /// </summary>
    private static async Task<IConnectionMultiplexer> WaitForRedisAsync(string connectionString)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        Exception? last = null;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                ConnectionMultiplexer mux = await ConnectionMultiplexer.ConnectAsync(
                    $"{connectionString},abortConnect=true,connectTimeout=2000");
                if (mux.IsConnected)
                {
                    await mux.GetDatabase().PingAsync();
                    return mux;
                }
                await mux.DisposeAsync();
            }
            catch (Exception ex)
            {
                last = ex;
            }
            await Task.Delay(150);
        }
        throw new InvalidOperationException("Redis did not become reachable in time.", last);
    }

    public void StubWebApiEcho()
    {
        _webApi.Given(Request.Create().WithPath("/echo").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""{"echoed":true}"""));
    }
}

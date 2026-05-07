using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Infra.Database;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace Web.API.IntegrationTests.Infrastructure;

/// <summary>
/// Boots Web.API behind a WireMock server impersonating Auth.API. The introspection
/// endpoint responds with `active=true` for tokens issued via <see cref="IssueTestToken"/>
/// and `active=false` for everything else, so authentication and authorization tests
/// don't need a live Auth.API or Redis cache.
/// </summary>
public sealed class CustomWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly string _dbName = $"IntegrationTestDb_{Guid.NewGuid()}";
    private WireMockServer _authApi = null!;

    public FakeMessagePublisher MessagePublisher { get; } = new();

    public WireMockServer AuthApi => _authApi;

    public Task InitializeAsync()
    {
        _authApi = WireMockServer.Start();
        StubAuthDiscovery();
        StubInactiveFallback();

        Environment.SetEnvironmentVariable(
            "DB_CONNECTION_STRING",
            "Host=localhost;Port=5432;Database=test;Username=test;Password=test;");
        Environment.SetEnvironmentVariable("Auth__Authority", _authApi.Url);
        Environment.SetEnvironmentVariable("Auth__IntrospectionEndpoint",
            $"{_authApi.Url}/connect/introspect");
        Environment.SetEnvironmentVariable("Auth__IntrospectionClientId", "web-api");
        Environment.SetEnvironmentVariable("Auth__IntrospectionClientSecret", "test-secret");
        // Skip Redis cache wiring (it's optional in AddIntrospectionAuthentication).
        Environment.SetEnvironmentVariable("Redis__ConnectionString", "");
        Environment.SetEnvironmentVariable("RABBITMQ_HOST", "localhost");
        Environment.SetEnvironmentVariable("RABBITMQ_USER", "test");
        Environment.SetEnvironmentVariable("RABBITMQ_PASSWORD", "test");
        return Task.CompletedTask;
    }

    public new Task DisposeAsync()
    {
        _authApi?.Stop();
        _authApi?.Dispose();
        return Task.CompletedTask;
    }

    Task IAsyncLifetime.DisposeAsync() => DisposeAsync();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Auth:Authority"] = _authApi.Url!,
                ["Auth:IntrospectionEndpoint"] = $"{_authApi.Url}/connect/introspect",
                ["Auth:IntrospectionClientId"] = "web-api",
                ["Auth:IntrospectionClientSecret"] = "test-secret",
                ["Redis:ConnectionString"] = "",
                ["ConnectionStrings:Database"] =
                    "Host=localhost;Port=5432;Database=test;Username=test;Password=test;",
                ["RabbitMq:Host"] = "localhost",
                ["RabbitMq:User"] = "test",
                ["RabbitMq:Password"] = "test",
                ["RabbitMq:ExchangeName"] = "test.exchange",
                ["RabbitMq:QueueName"] = "test.queue",
                ["RabbitMq:RoutingKey"] = "test.key"
            });
        });

        builder.ConfigureServices(services =>
        {
            List<ServiceDescriptor> efDescriptors = services
                .Where(d => d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>)
                            || d.ServiceType == typeof(ApplicationDbContext)
                            || (d.ServiceType.FullName?.StartsWith("Microsoft.EntityFrameworkCore") ?? false))
                .ToList();

            foreach (ServiceDescriptor descriptor in efDescriptors)
            {
                services.Remove(descriptor);
            }

            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseInMemoryDatabase(_dbName));

            ServiceDescriptor? publisherDescriptor = services
                .FirstOrDefault(d => d.ServiceType == typeof(IMessagePublisher));
            if (publisherDescriptor is not null)
            {
                services.Remove(publisherDescriptor);
            }

            services.AddSingleton<IMessagePublisher>(MessagePublisher);
        });
    }

    /// <summary>
    /// Issues a test token by stubbing WireMock's introspection endpoint to respond
    /// `active=true` for the returned token string. The principal carries
    /// <paramref name="subjectId"/>, <paramref name="tenantId"/>, and the requested
    /// permission claims.
    /// </summary>
    public string IssueTestToken(
        Guid? tenantId = null,
        string subjectId = "integration-user",
        params string[] permissions)
    {
        string token = $"test-token-{Guid.NewGuid():N}";
        StubIntrospectionActive(token, subjectId, tenantId ?? Guid.NewGuid(), permissions);
        return token;
    }

    public void StubIntrospectionActive(
        string token,
        string subjectId,
        Guid tenantId,
        params string[] permissions)
    {
        // Build a JSON object with optional permission claims as repeated keys; OpenIddict
        // accepts a JSON array under "permission".
        string permissionsJson = permissions.Length == 0
            ? string.Empty
            : $", \"permission\": [{string.Join(",", permissions.Select(p => $"\"{p}\""))}]";

        string json = $$"""
            {
              "active": true,
              "sub": "{{subjectId}}",
              "tenant_id": "{{tenantId}}",
              "aud": ["api:web"],
              "iss": "{{_authApi.Url}}/",
              "token_usage": "access_token"{{permissionsJson}}
            }
            """;

        _authApi.Given(Request.Create()
                .WithPath("/connect/introspect")
                .WithBody(b => b != null && b.Contains($"token={token}"))
                .UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(json));
    }

    public void StubIntrospectionInactive(string token)
    {
        _authApi.Given(Request.Create()
                .WithPath("/connect/introspect")
                .WithBody(b => b != null && b.Contains($"token={token}"))
                .UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""{"active":false}"""));
    }

    private void StubInactiveFallback()
    {
        // Default: any introspection request not previously stubbed yields active=false.
        _authApi.Given(Request.Create()
                .WithPath("/connect/introspect")
                .UsingPost())
            .AtPriority(int.MaxValue)
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""{"active":false}"""));
    }

    private void StubAuthDiscovery()
    {
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
            token_endpoint_auth_methods_supported = new[] { "client_secret_basic", "client_secret_post" },
            introspection_endpoint_auth_methods_supported = new[] { "client_secret_basic", "client_secret_post" },
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

        _authApi.Given(Request.Create().WithPath("/.well-known/openid-configuration").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBodyAsJson(discovery));

        _authApi.Given(Request.Create().WithPath("/.well-known/jwks").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBodyAsJson(jwks));
    }
}

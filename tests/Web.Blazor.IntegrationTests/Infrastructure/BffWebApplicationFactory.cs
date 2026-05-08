using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using StackExchange.Redis;
using Testcontainers.Redis;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace Web.Blazor.IntegrationTests.Infrastructure;

/// <summary>
/// Boots Web.Blazor (the BFF) behind a real Redis container plus two WireMock servers:
/// one impersonating Auth.API (OIDC discovery / authorize / token / jwks) and another
/// acting as the Gateway (bouncing /api/auth/* back to the WireMock Auth.API for admin
/// pages).
///
/// Notes / lessons learned from earlier bundles:
///  - parallel xUnit fixtures stomp on process-wide env vars; we share a single fixture
///    via <see cref="BffCollection"/>.
///  - the OIDC handler insists on a JWKS url and validates id_token signatures, so we
///    generate a real RSA key once and serve a matching JWKS document.
///  - tests can either drive the full OIDC dance (supported via <see cref="WireMock.Server.WireMockServer.LogEntries"/>
///    inspection) or skip it entirely with <see cref="SetTestSessionCookieAsync"/>, which
///    seeds a session id directly into the Redis-backed token store.
/// </summary>
public sealed class BffWebApplicationFactory
    : WebApplicationFactory<Web.Blazor.Program>, IAsyncLifetime
{
    private readonly RedisContainer _redis = new RedisBuilder()
        .WithImage("redis:7-alpine")
        .Build();

    private WireMockServer _authApi = null!;
    private WireMockServer _gateway = null!;
    private RSA _signingKey = null!;
    private string _kid = null!;
    private IConnectionMultiplexer? _redisMultiplexer;

    public WireMockServer AuthApi => _authApi;
    public WireMockServer Gateway => _gateway;
    public string AuthorityUrl => _authApi.Url!;
    public string GatewayUrl => _gateway.Url!;
    public static string ClientId => "bff-blazor";
    public static string ClientSecret => "dev-only-bff-secret-change-me";

    public async Task InitializeAsync()
    {
        await _redis.StartAsync();
        _redisMultiplexer = await WaitForRedisAsync(_redis.GetConnectionString());

        _signingKey = RSA.Create(2048);
        _kid = "bff-test-key-1";

        _authApi = WireMockServer.Start();
        _gateway = WireMockServer.Start();

        StubDiscovery();
        StubAuthorize();
        StubToken();
        StubUserInfo();
        StubGatewayPassthrough();

        // Web.Blazor's Program.cs reads Redis:ConnectionString synchronously at start-up
        // (await ConnectionMultiplexer.ConnectAsync). Mirror values into env vars so the
        // host build sees them even before ConfigureAppConfiguration runs.
        Environment.SetEnvironmentVariable("Redis__ConnectionString",
            $"{_redis.GetConnectionString()},abortConnect=false,connectRetry=5,connectTimeout=10000");
        Environment.SetEnvironmentVariable("Auth__Authority", _authApi.Url);
        Environment.SetEnvironmentVariable("Auth__ClientId", ClientId);
        Environment.SetEnvironmentVariable("Auth__ClientSecret", ClientSecret);
        Environment.SetEnvironmentVariable("Gateway__BaseUrl", _gateway.Url);
    }

    public new async Task DisposeAsync()
    {
        _authApi.Stop();
        _authApi.Dispose();
        _gateway.Stop();
        _gateway.Dispose();
        _signingKey.Dispose();
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

        builder.ConfigureAppConfiguration((_, cfg) =>
        {
            cfg.Sources.Add(new Microsoft.Extensions.Configuration.Memory.MemoryConfigurationSource
            {
                InitialData = new Dictionary<string, string?>
                {
                    ["Redis:ConnectionString"] =
                        $"{_redis.GetConnectionString()},abortConnect=false,connectRetry=5,connectTimeout=10000",
                    ["Auth:Authority"] = _authApi.Url!,
                    ["Auth:ClientId"] = ClientId,
                    ["Auth:ClientSecret"] = ClientSecret,
                    ["Gateway:BaseUrl"] = _gateway.Url!,
                    ["Logging:LogLevel:Default"] = "Warning",
                }
            });
        });
    }

    /// <summary>
    /// Bypass the full OIDC dance and seed a session directly into the Redis-backed token
    /// store. Returns the session id which callers can attach as a cookie if needed.
    /// </summary>
    public async Task<string> SetTestSessionCookieAsync(
        string subjectId,
        params string[] permissions)
    {
        string sessionId = Guid.NewGuid().ToString("N");
        IConnectionMultiplexer mux = _redisMultiplexer
            ?? throw new InvalidOperationException("Redis multiplexer not initialised.");

        // Match the layout the production RedisTokenStore writes.
        string key = $"bff:cache:bff:session:{sessionId}";
        var tokens = new
        {
            AccessToken = "test-access-token",
            RefreshToken = "test-refresh-token",
            IdToken = (string?)null,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(15),
        };
        byte[] payload = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(tokens);
        await mux.GetDatabase().StringSetAsync(key, payload, TimeSpan.FromHours(8));

        _ = subjectId;
        _ = permissions;
        return sessionId;
    }

    /// <summary>
    /// Mint an RS256 id_token signed with the same RSA key advertised in the JWKS document.
    /// Useful when a test wants to drive the OIDC handler past signature validation.
    /// </summary>
    public string MintIdToken(string subjectId, IEnumerable<System.Security.Claims.Claim>? extraClaims = null)
    {
        var key = new RsaSecurityKey(_signingKey) { KeyId = _kid };
        var creds = new SigningCredentials(key, SecurityAlgorithms.RsaSha256);
        var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
        var now = DateTime.UtcNow;
        var claims = new List<System.Security.Claims.Claim>
        {
            new("sub", subjectId),
            new("iss", _authApi.Url!),
            new("aud", ClientId),
            new("nonce", "test-nonce"),
        };
        if (extraClaims is not null)
        {
            claims.AddRange(extraClaims);
        }

        var token = handler.CreateJwtSecurityToken(
            issuer: _authApi.Url,
            audience: ClientId,
            subject: new System.Security.Claims.ClaimsIdentity(claims),
            notBefore: now,
            expires: now.AddMinutes(15),
            issuedAt: now,
            signingCredentials: creds);
        return handler.WriteToken(token);
    }

    private void StubDiscovery()
    {
        RSAParameters parms = _signingKey.ExportParameters(false);
        string n = Base64UrlEncoder.Encode(parms.Modulus!);
        string e = Base64UrlEncoder.Encode(parms.Exponent!);

        var discovery = new
        {
            issuer = _authApi.Url,
            authorization_endpoint = $"{_authApi.Url}/oauth2/authorize",
            token_endpoint = $"{_authApi.Url}/connect/token",
            userinfo_endpoint = $"{_authApi.Url}/connect/userinfo",
            end_session_endpoint = $"{_authApi.Url}/connect/logout",
            jwks_uri = $"{_authApi.Url}/.well-known/jwks.json",
            response_types_supported = new[] { "code" },
            grant_types_supported = new[] { "authorization_code", "refresh_token" },
            subject_types_supported = new[] { "public" },
            id_token_signing_alg_values_supported = new[] { "RS256" },
            scopes_supported = new[] { "openid", "profile", "email", "offline_access" },
            token_endpoint_auth_methods_supported = new[] { "client_secret_post", "client_secret_basic" },
            code_challenge_methods_supported = new[] { "S256" },
        };

        var jwks = new
        {
            keys = new object[]
            {
                new { kty = "RSA", use = "sig", alg = "RS256", kid = _kid, n, e },
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
                .WithPath("/.well-known/jwks.json")
                .UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBodyAsJson(jwks));
    }

    private void StubAuthorize()
    {
        // Anything hitting /oauth2/authorize is treated as a successful login: redirect back
        // to the BFF callback with a fixed `code`. Tests that follow redirects can use this
        // to drive the OIDC handler past the authorize step.
        _authApi.Given(Request.Create().WithPath("/oauth2/authorize").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(302)
                .WithHeader("Location",
                    "/signin-oidc?code=test-code&state={{request.query.state}}")
                .WithTransformer());
    }

    private void StubToken()
    {
        // Token endpoint returns a fresh id_token signed by our RSA key. Access/refresh
        // tokens are opaque strings; the BFF stores them in Redis but never validates them
        // locally, so the values do not need to be JWTs.
        string idToken = MintIdToken("test-user");
        var body = new
        {
            access_token = "test-access-token",
            refresh_token = "test-refresh-token",
            id_token = idToken,
            token_type = "Bearer",
            expires_in = 900,
            scope = "openid profile email offline_access",
        };
        _authApi.Given(Request.Create().WithPath("/connect/token").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBodyAsJson(body));
    }

    private void StubUserInfo()
    {
        _authApi.Given(Request.Create().WithPath("/connect/userinfo").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBodyAsJson(new { sub = "test-user", name = "Test User", email = "test@example.com" }));
    }

    private void StubGatewayPassthrough()
    {
        // Default catch-all for the gateway: respond 200 with an empty paged response so
        // admin pages can render without exploding. Individual tests should override with
        // their own stubs as needed.
        _gateway.Given(Request.Create().WithPath("/api/auth/admin/*").UsingAnyMethod())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""{"items":[],"total":0,"page":1,"pageSize":20}"""));
    }

    private static async Task<IConnectionMultiplexer> WaitForRedisAsync(string connectionString)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(20);
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
}

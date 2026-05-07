using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using Auth.Application.Abstractions.Crypto;
using Auth.Domain.M2MClients;
using Auth.Domain.Permissions;
using Auth.Domain.Roles;
using Auth.Domain.Tenants;
using Auth.Domain.Users;
using Auth.Application.Abstractions.Tenancy;
using Auth.Infra.Database;
using Auth.Infra.Database.Joins;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using OpenIddict.Server.AspNetCore;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace Auth.API.IntegrationTests.Infrastructure;

/// <summary>
/// Integration test fixture that boots the Auth.API behind a real Postgres + Redis pair
/// (via Testcontainers) and a WireMock-faked Entra ID. Migrations are applied during
/// <see cref="InitializeAsync"/> so each fixture instance starts from a fresh schema.
/// </summary>
public sealed class AuthWebApplicationFactory : WebApplicationFactory<Auth.API.Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("auth_db")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    private readonly RedisContainer _redis = new RedisBuilder()
        .WithImage("redis:7-alpine")
        .Build();

    private WireMockServer _entra = null!;
    private RSA _testRsa = null!;
    private RsaSecurityKey _testSigningKey = null!;
    private SigningCredentials _testSigningCredentials = null!;

    public WireMockServer Entra => _entra;
    public string EntraBaseUrl => _entra.Url ?? throw new InvalidOperationException("WireMock not started");

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await _redis.StartAsync();

        // Mirror connection settings into env vars so Auth.Infra picks them up regardless of
        // when ConfigureAppConfiguration is layered into the host pipeline. ASP.NET Core's
        // configuration provider order makes env vars beat appsettings*.json, which is what
        // we want to override the dev defaults baked into appsettings.Development.json.
        Environment.SetEnvironmentVariable("ConnectionStrings__AuthDb", _postgres.GetConnectionString());
        Environment.SetEnvironmentVariable(
            "Redis__ConnectionString",
            $"{_redis.GetConnectionString()},abortConnect=false");

        _testRsa = RSA.Create(2048);
        _testSigningKey = new RsaSecurityKey(_testRsa) { KeyId = "test-key-1" };
        _testSigningCredentials = new SigningCredentials(_testSigningKey, SecurityAlgorithms.RsaSha256);

        _entra = WireMockServer.Start();
        StubEntraDiscovery();
        StubEntraJwks();
        StubEntraAuthorizeRedirect();
        StubEntraTokenDefault();

        // Build the schema BEFORE the WebApplicationFactory boots its host (and triggers
        // PermissionSeedHostedService / OpenIddictClientSeedHostedService). We use a tiny
        // standalone service provider that mirrors AuthDbContext's registration plus
        // OpenIddict's EF Core integration so the model contains both our entities AND
        // OpenIddict's. EnsureCreated is sufficient — this is a throwaway test database.
        await EnsureSchemaStandaloneAsync();
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        _entra.Stop();
        _entra.Dispose();
        _testRsa.Dispose();
        await _postgres.DisposeAsync();
        await _redis.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, cfg) =>
        {
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:AuthDb"] = _postgres.GetConnectionString(),
                // abortConnect=false lets StackExchange.Redis keep retrying briefly while the
                // Testcontainers Redis is finishing its boot handshake.
                ["Redis:ConnectionString"] = $"{_redis.GetConnectionString()},abortConnect=false",
                ["Entra:Authority"] = $"{EntraBaseUrl}/v2.0/",
                ["Entra:ClientId"] = "test-client",
                ["Entra:ClientSecret"] = "test-secret",
                // Permissive logging for diagnostics if a test fails.
                ["Logging:LogLevel:Default"] = "Warning",
            });
        });

        builder.ConfigureServices(services =>
        {
            // Make the Entra OIDC handler tolerate an http metadata endpoint and pull
            // discovery from our WireMock server. We disable HTTPS metadata enforcement
            // because the WireMock server only listens on http.
            services.PostConfigure<OpenIdConnectOptions>("Entra", opt =>
            {
                opt.MetadataAddress = $"{EntraBaseUrl}/v2.0/.well-known/openid-configuration";
                opt.RequireHttpsMetadata = false;
                opt.BackchannelHttpHandler = new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
                };
            });

            // The TestServer speaks HTTP. Disable OpenIddict's transport-security gate so
            // /connect/token et al. accept the in-memory http requests.
            services.PostConfigure<OpenIddictServerAspNetCoreOptions>(opt =>
            {
                opt.DisableTransportSecurityRequirement = true;
            });
        });
    }

    private async Task EnsureSchemaStandaloneAsync()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ITenantContext>(new NoOpTenantContext());
        services.AddDbContext<AuthDbContext>(opt =>
        {
            opt.UseNpgsql(_postgres.GetConnectionString(),
                npg => npg.MigrationsHistoryTable("__ef_migrations_history", Schemas.Auth));
            opt.UseSnakeCaseNamingConvention();
        });

        // Wire OpenIddict EF Core so its entities appear in the model and EnsureCreated
        // generates the openiddict_* tables alongside our auth schema.
        services.AddOpenIddict()
            .AddCore(o => o.UseEntityFrameworkCore().UseDbContext<AuthDbContext>());

        await using ServiceProvider sp = services.BuildServiceProvider();
        using IServiceScope scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        await db.Database.EnsureCreatedAsync();
    }

    private sealed class NoOpTenantContext : ITenantContext
    {
        public bool HasTenant => false;
        public Guid TenantId => Guid.Empty;
    }

    // ---------- WireMock stubs ----------

    private void StubEntraDiscovery()
    {
        string baseUrl = EntraBaseUrl;
        var discovery = new
        {
            issuer = $"{baseUrl}/v2.0",
            authorization_endpoint = $"{baseUrl}/oauth2/v2.0/authorize",
            token_endpoint = $"{baseUrl}/oauth2/v2.0/token",
            jwks_uri = $"{baseUrl}/discovery/v2.0/keys",
            userinfo_endpoint = $"{baseUrl}/oidc/userinfo",
            end_session_endpoint = $"{baseUrl}/oauth2/v2.0/logout",
            response_types_supported = new[] { "code", "id_token", "code id_token" },
            subject_types_supported = new[] { "pairwise" },
            id_token_signing_alg_values_supported = new[] { "RS256" },
            scopes_supported = new[] { "openid", "profile", "email", "offline_access" },
            token_endpoint_auth_methods_supported = new[] { "client_secret_post", "client_secret_basic" },
            claims_supported = new[] { "sub", "iss", "aud", "exp", "iat", "tid", "oid", "email", "name", "preferred_username" },
        };

        _entra
            .Given(Request.Create().WithPath("/v2.0/.well-known/openid-configuration").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(discovery));
    }

    private void StubEntraJwks()
    {
        RSAParameters p = _testRsa.ExportParameters(includePrivateParameters: false);
        var jwks = new
        {
            keys = new object[]
            {
                new
                {
                    kty = "RSA",
                    use = "sig",
                    kid = _testSigningKey.KeyId,
                    alg = "RS256",
                    n = Base64UrlEncoder.Encode(p.Modulus!),
                    e = Base64UrlEncoder.Encode(p.Exponent!),
                },
            },
        };

        _entra
            .Given(Request.Create().WithPath("/discovery/v2.0/keys").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(jwks));
    }

    private void StubEntraAuthorizeRedirect()
    {
        // The OIDC handler will redirect the user to /oauth2/v2.0/authorize. We immediately
        // bounce back to Auth.API's /signin-entra callback with a synthetic code+state.
        // Tests that rely on a different identity should call StubEntraUserLogin first.
        _entra
            .Given(Request.Create().WithPath("/oauth2/v2.0/authorize").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode((int)HttpStatusCode.Found)
                .WithTransformer()
                .WithHeader("Location", "{{request.query.redirect_uri}}?code=fake-code&state={{request.query.state}}"));
    }

    private void StubEntraTokenDefault()
    {
        // Default identity: random oid+tid+email. Tests that need a specific user should
        // call StubEntraUserLogin explicitly to override this stub.
        StubEntraTokenInternal(
            oid: Guid.NewGuid(),
            tid: Guid.NewGuid(),
            email: "default@example.com",
            name: "Default Test User");
    }

    private void StubEntraTokenInternal(Guid oid, Guid tid, string email, string name)
    {
        string idToken = IssueIdToken(oid, tid, email, name);
        var token = new
        {
            token_type = "Bearer",
            scope = "openid profile email",
            expires_in = 3600,
            access_token = "fake-entra-access-token",
            id_token = idToken,
        };

        _entra
            .Given(Request.Create().WithPath("/oauth2/v2.0/token").UsingPost())
            .AtPriority(1)
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(token));
    }

    /// <summary>
    /// Configure WireMock so the next federated login flow yields an id_token with the
    /// supplied identity claims. Subsequent calls override previous stubs.
    /// </summary>
    public void StubEntraUserLogin(Guid oid, Guid tid, string email, string name = "Test User")
    {
        StubEntraTokenInternal(oid, tid, email, name);
    }

    private string IssueIdToken(Guid oid, Guid tid, string email, string name)
    {
        var handler = new JwtSecurityTokenHandler();
        var now = DateTime.UtcNow;
        var token = handler.CreateJwtSecurityToken(
            issuer: $"{EntraBaseUrl}/v2.0",
            audience: "test-client",
            subject: new ClaimsIdentity([
                new Claim("sub", oid.ToString()),
                new Claim("oid", oid.ToString()),
                new Claim("tid", tid.ToString()),
                new Claim("email", email),
                new Claim("preferred_username", email),
                new Claim("name", name),
            ]),
            notBefore: now,
            expires: now.AddMinutes(60),
            issuedAt: now,
            signingCredentials: _testSigningCredentials);
        return handler.WriteToken(token);
    }

    // ---------- Seed helpers ----------

    public async Task<Tenant> SeedTenantAsync(
        Guid entraTenantId,
        string displayName = "Test Tenant",
        string redirectUri = "https://localhost/signin-oidc",
        bool active = true)
    {
        using IServiceScope scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        Tenant tenant = Tenant.Create(entraTenantId, displayName, redirectUri);
        if (!active)
        {
            tenant.Deactivate();
        }
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        return tenant;
    }

    public async Task<User> SeedUserAsync(
        Guid tenantId,
        Guid entraOid,
        string email,
        bool isPreProvisioned = false,
        bool isActive = true)
    {
        using IServiceScope scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();

        User user = isPreProvisioned
            ? User.PreProvision(tenantId, email, email)
            : User.ProvisionFromEntra(tenantId, entraOid, email, email);

        if (!isActive && user.IsActive)
        {
            user.Disable();
        }

        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    public async Task<Role> SeedRoleAsync(Guid tenantId, string name, params string[] permissionCodes)
    {
        using IServiceScope scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();

        Role role = Role.Create(tenantId, name, $"{name} role");
        db.Roles.Add(role);
        await db.SaveChangesAsync();

        if (permissionCodes.Length == 0)
        {
            return role;
        }

        Permission[] perms = await db.Permissions
            .Where(p => permissionCodes.Contains(p.Code))
            .ToArrayAsync();

        foreach (Permission p in perms)
        {
            role.AssignPermission(p.Id);
        }

        await db.SaveChangesAsync();
        return role;
    }

    public async Task AssignRoleToUserAsync(Guid userId, Guid roleId)
    {
        using IServiceScope scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();

        User user = await db.Users.FirstAsync(u => u.Id == userId);
        user.AssignRole(roleId);
        await db.SaveChangesAsync();
    }

    public async Task<M2MClient> SeedM2MClientAsync(
        Guid tenantId,
        string clientId,
        string secret,
        params string[] scopes)
    {
        using IServiceScope scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IClientSecretHasher>();

        M2MClient client = M2MClient.Register(tenantId, clientId, hasher.Hash(secret), clientId, scopes);
        db.M2MClients.Add(client);
        await db.SaveChangesAsync();

        // Mirror as an OpenIddict application so /connect/token can drive the
        // client_credentials flow through OpenIddict's permission gate.
        var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        if (await manager.FindByClientIdAsync(clientId) is null)
        {
            var descriptor = new OpenIddictApplicationDescriptor
            {
                ClientId = clientId,
                ClientType = OpenIddictConstants.ClientTypes.Confidential,
                ClientSecret = secret,
                DisplayName = clientId,
                Permissions =
                {
                    OpenIddictConstants.Permissions.Endpoints.Token,
                    OpenIddictConstants.Permissions.Endpoints.Introspection,
                    OpenIddictConstants.Permissions.Endpoints.Revocation,
                    OpenIddictConstants.Permissions.GrantTypes.ClientCredentials,
                },
            };
            foreach (string s in scopes)
            {
                descriptor.Permissions.Add(OpenIddictConstants.Permissions.Prefixes.Scope + s);
            }
            await manager.CreateAsync(descriptor);
        }

        return client;
    }

    public async Task DeactivateM2MClientAsync(string clientId)
    {
        using IServiceScope scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        M2MClient client = await db.M2MClients.FirstAsync(c => c.ClientId == clientId);
        client.Deactivate();
        await db.SaveChangesAsync();
    }
}

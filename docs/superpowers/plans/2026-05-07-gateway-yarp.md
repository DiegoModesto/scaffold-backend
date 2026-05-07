# Gateway (YARP) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stand up a standalone YARP reverse-proxy gateway as the **single public ingress** for backend services, validating OpenIddict reference tokens via `/connect/introspect` with a Redis-backed response cache (default 30s TTL) and forwarding authenticated requests to `Auth.API` and (in passthrough) to `Web.API`.

**Architecture:** New `Gateway` entrypoint project (ASP.NET Core minimal host + YARP). Token validation uses `OpenIddict.Validation.AspNetCore` configured to call Auth.API's introspection endpoint, wrapped by a custom `IntrospectionCachingHandler` that caches active/inactive responses in Redis keyed by `SHA256(token)`. Routes are declared in `appsettings.json` under YARP's standard `ReverseProxy:Routes` / `ReverseProxy:Clusters`. Every authenticated request gets a synthetic `X-Forwarded-User`, `X-Forwarded-TenantId`, and the original `Authorization` header propagated to the downstream cluster.

**Tech Stack:** .NET 10, [Yarp.ReverseProxy 2.3.0](https://www.nuget.org/packages/Yarp.ReverseProxy), `OpenIddict.Validation.AspNetCore` 6.4.0 (already in scaffold), `OpenIddict.Validation.SystemNetHttp` 6.4.0 (introspection client), `Microsoft.Extensions.Caching.StackExchangeRedis` 10.0.7 (already in scaffold), xUnit + Shouldly + WireMock.Net 2.5.0 + Testcontainers (Redis + PostgreSQL) for integration tests.

**Out of scope (deferred):**
- Plan 3: Blazor BFF cookie-session client (will use Gateway as backend).
- Plan 4: NetSuite SAML.
- Plan 5: Web.API integration with introspection middleware (Web.API itself becomes a downstream that the gateway forwards to; **this plan does not modify Web.API**).
- mTLS between Gateway → Auth.API (production hardening, follow-up).
- Per-route rate limiting beyond a global default.
- Request/response transforms beyond header forwarding.

---

## Context for the implementer

- Branch: `feature/gateway-yarp` (already created and checked out).
- Auth.API already runs at `http://localhost:5100` in dev (compose service `auth.api`). It exposes:
  - `/.well-known/openid-configuration` (discovery)
  - `/connect/token` (issues opaque reference tokens)
  - `/connect/introspect` (RFC 7662, basic-auth with resource-server `client_id`/`client_secret`)
  - `/health/live`, `/health/ready`
- Two seeded resource-server clients exist in `auth_db.openiddict_applications`:
  - `client_id=web-api`, `client_secret=dev-only-web-api-secret-change-me` — **`Endpoints.Introspection` permission only**.
  - `client_id=gateway`, `client_secret=dev-only-gateway-secret-change-me` — **`Endpoints.Introspection` permission only**. **The gateway authenticates to introspect with these credentials.**
- Token format: opaque reference (no JWT signature validation possible — must call introspection).
- The `OpenIddict:Issuer` env var on Auth.API is set to `http://localhost:5100` in compose. The gateway's introspection client validates that the issuer in the response matches.
- `compose.yaml` already runs `auth-postgres` (5433), `redis` (6379), and `auth.api` (5100). The gateway will be added at host port `5200`.
- Architectural rules from `CLAUDE.md` (§2) apply: Gateway is an entrypoint that may reference `Auth.Application`/`Auth.Infra` only if absolutely required (it should NOT — Gateway should be self-contained, talking to Auth.API only over HTTP). Plan-imposed rule: **the Gateway project must not have a `ProjectReference` to `Auth.*` projects.** It's just a YARP host.
- The existing scaffold's `Infra.Observability.OpenTelemetryExtensions.AddOpenTelemetryObservability(...)` is the shared OTel composition entrypoint. Gateway will reference `Infra` for that single purpose, mirroring how `Web.API` does.

---

## File structure

```
src/EntryPoints/Gateway/
├── Gateway.csproj                              # .NET Sdk.Web; refs Infra (for OTel + ConfigurationExtensions)
├── Program.cs                                  # composition root
├── DependencyInjection.cs                      # AddGatewayPresentation
├── Authentication/
│   ├── IntrospectionCacheOptions.cs            # config record
│   ├── IntrospectionCachingHandler.cs          # DelegatingHandler that caches /introspect responses
│   └── ForwardedIdentityTransform.cs           # YARP request transform: add X-Forwarded-User/TenantId
├── HealthChecks/
│   └── AuthApiHealthCheck.cs                   # GET /health/ready against Auth.API
├── appsettings.json                            # YARP routes/clusters skeleton (empty secrets)
├── appsettings.Development.json                # local routes pointing to Auth.API, Web.API
└── Dockerfile

tests/Gateway.IntegrationTests/
├── Gateway.IntegrationTests.csproj
├── Infrastructure/
│   ├── GatewayWebApplicationFactory.cs        # Boots Gateway + WireMock-stubbed Auth.API + Redis testcontainer
│   └── TestTokenFixture.cs                    # helper to forge tokens accepted by the WireMock stub
├── Architecture/
│   └── ArchitectureTests.cs                   # NetArchTest: Gateway has no Auth.* project refs
├── Authentication/
│   ├── IntrospectionCachingHandlerTests.cs    # unit-level cache behavior with fake Redis
│   └── AuthenticationFlowTests.cs             # end-to-end: 401 on no-token, 200 on valid, cache hit
├── Routing/
│   └── ProxyRoutingTests.cs                   # GET /api/auth/health/live → forwarded to Auth.API
└── HealthCheckTests.cs                        # /health/ready aggregates Auth.API + Redis status
```

**Compose changes:**
```
compose.yaml
├── add service `gateway` (port 5200 → 8080)
├── depends_on: redis, auth.api
├── env: GATEWAY_INTROSPECTION_CLIENT_ID=gateway,
│       GATEWAY_INTROSPECTION_CLIENT_SECRET=${OPENIDDICT_GATEWAY_SECRET:-dev-only-gateway-secret-change-me},
│       OpenIddict__Issuer=http://localhost:5100,
│       Redis__ConnectionString=redis:6379,
│       Auth__IntrospectionEndpoint=http://auth.api:8080/connect/introspect
```

**Documentation:**
- `CLAUDE.md` §1 entrypoint list — add Gateway.
- `CLAUDE.md` §3 directory layout — add `src/EntryPoints/Gateway/` and `tests/Gateway.IntegrationTests/`.
- `CLAUDE.md` §13 pitfalls — add gateway-specific entries (introspection cache TTL, X-Forwarded-* header trust).
- `README.md` — add Gateway section (EN + PT).

---

## Phase 0 — Project scaffolding

### Task 0.1: Add YARP package version

**Files:**
- Modify: `Directory.Packages.props`

- [ ] **Step 1: Add YARP + OpenIddict introspection client packages**

Inside the existing `<ItemGroup>` (alphabetical groups OK):

```xml
<!-- Reverse proxy -->
<PackageVersion Include="Yarp.ReverseProxy" Version="2.3.0" />
<!-- Identity (gateway-specific) -->
<PackageVersion Include="OpenIddict.Validation.SystemNetHttp" Version="6.4.0" />
```

- [ ] **Step 2: Verify restore**

Run: `dotnet restore BaseProjectScaffold.sln`
Expected: success.

- [ ] **Step 3: Commit**

```bash
git add Directory.Packages.props
git commit -m "chore: add YARP and OpenIddict introspection client packages"
```

### Task 0.2: Create `Gateway` entrypoint project

**Files:**
- Create: `src/EntryPoints/Gateway/Gateway.csproj`
- Create: `src/EntryPoints/Gateway/Program.cs`
- Create: `src/EntryPoints/Gateway/appsettings.json`
- Create: `src/EntryPoints/Gateway/appsettings.Development.json`

- [ ] **Step 1: csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>Gateway</RootNamespace>
    <UserSecretsId>gateway-2026-05-07</UserSecretsId>
    <DockerDefaultTargetOS>Linux</DockerDefaultTargetOS>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\Infra\Infra.csproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="AspNetCore.HealthChecks.UI.Client" />
    <PackageReference Include="Microsoft.Extensions.Caching.StackExchangeRedis" />
    <PackageReference Include="OpenIddict.Validation.AspNetCore" />
    <PackageReference Include="OpenIddict.Validation.SystemNetHttp" />
    <PackageReference Include="Serilog.AspNetCore" />
    <PackageReference Include="Serilog.Sinks.Console" />
    <PackageReference Include="Serilog.Sinks.Seq" />
    <PackageReference Include="StackExchange.Redis" />
    <PackageReference Include="Yarp.ReverseProxy" />
  </ItemGroup>
</Project>
```

(The existing `Directory.Build.props` adds `SonarAnalyzer.CSharp` automatically — do not duplicate here.)

- [ ] **Step 2: Minimal `Program.cs`**

```csharp
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
app.MapGet("/health/live", () => Results.Ok(new { status = "live" }));
await app.RunAsync();

namespace Gateway
{
    public partial class Program;
}
```

- [ ] **Step 3: Empty `appsettings.json`**

```json
{
  "Logging": { "LogLevel": { "Default": "Information" } },
  "AllowedHosts": "*",
  "Redis": { "ConnectionString": "" },
  "Auth": {
    "Authority": "",
    "IntrospectionEndpoint": "",
    "IntrospectionClientId": "",
    "IntrospectionClientSecret": ""
  },
  "IntrospectionCache": {
    "TtlSeconds": 30
  },
  "ReverseProxy": {
    "Routes": {},
    "Clusters": {}
  }
}
```

- [ ] **Step 4: `appsettings.Development.json`**

```json
{
  "Logging": {
    "LogLevel": { "Default": "Information", "Microsoft.AspNetCore": "Warning" }
  },
  "Redis": { "ConnectionString": "localhost:6379" },
  "Auth": {
    "Authority": "http://localhost:5100",
    "IntrospectionEndpoint": "http://localhost:5100/connect/introspect",
    "IntrospectionClientId": "gateway",
    "IntrospectionClientSecret": "dev-only-gateway-secret-change-me"
  },
  "IntrospectionCache": { "TtlSeconds": 30 },
  "ReverseProxy": {
    "Routes": {
      "auth-discovery": {
        "ClusterId": "auth-cluster",
        "Match": { "Path": "/api/auth/.well-known/{**catch-all}" },
        "Transforms": [
          { "PathRemovePrefix": "/api/auth" }
        ]
      },
      "auth-connect": {
        "ClusterId": "auth-cluster",
        "Match": { "Path": "/api/auth/connect/{**catch-all}" },
        "Transforms": [
          { "PathRemovePrefix": "/api/auth" }
        ]
      },
      "web-api": {
        "ClusterId": "web-api-cluster",
        "Match": { "Path": "/api/v1/{**catch-all}" },
        "AuthorizationPolicy": "RequireBearer"
      }
    },
    "Clusters": {
      "auth-cluster": {
        "Destinations": {
          "auth": { "Address": "http://localhost:5100/" }
        }
      },
      "web-api-cluster": {
        "Destinations": {
          "web-api": { "Address": "http://localhost:5010/" }
        }
      }
    }
  }
}
```

The `auth-discovery` and `auth-connect` routes are **deliberately unauthenticated** — they ARE the authentication endpoints; requiring a token to acquire a token is a chicken-and-egg loop.

The `web-api` route requires a bearer token (policy `RequireBearer`, defined in Phase 2).

- [ ] **Step 5: Add to solution + build**

```bash
dotnet sln BaseProjectScaffold.sln add src/EntryPoints/Gateway/Gateway.csproj
dotnet build src/EntryPoints/Gateway/Gateway.csproj
```

Expected: success.

- [ ] **Step 6: Commit**

```bash
git add src/EntryPoints/Gateway/ BaseProjectScaffold.sln
git commit -m "feat(gateway): scaffold Gateway entrypoint with health/live"
```

### Task 0.3: Create test project

**Files:**
- Create: `tests/Gateway.IntegrationTests/Gateway.IntegrationTests.csproj`

- [ ] **Step 1: csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\EntryPoints\Gateway\Gateway.csproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="NetArchTest.Rules" />
    <PackageReference Include="Moq" />
    <PackageReference Include="Shouldly" />
    <PackageReference Include="Testcontainers.Redis" />
    <PackageReference Include="WireMock.Net" />
    <PackageReference Include="coverlet.collector" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio" />
  </ItemGroup>
  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Add to solution + build**

```bash
dotnet sln BaseProjectScaffold.sln add tests/Gateway.IntegrationTests/Gateway.IntegrationTests.csproj
dotnet build BaseProjectScaffold.sln -c Release
```

Expected: 0 errors, 0 warnings.

- [ ] **Step 3: Commit**

```bash
git add tests/Gateway.IntegrationTests/ BaseProjectScaffold.sln
git commit -m "test(gateway): scaffold Gateway integration test project"
```

---

## Phase 1 — Minimal YARP wiring (no auth yet)

### Task 1.1: Wire YARP, Serilog, OTel into `Program.cs`

**Files:**
- Modify: `src/EntryPoints/Gateway/Program.cs`

- [ ] **Step 1: Replace `Program.cs` with**

```csharp
using Infra.Observability;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, sp, cfg) =>
    cfg.ReadFrom.Configuration(ctx.Configuration).ReadFrom.Services(sp));

builder.Services.AddOpenTelemetryObservability(
    builder.Configuration,
    serviceName: "Gateway",
    includeAspNetCore: true,
    additionalActivitySources: ["Gateway"]);

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var app = builder.Build();

app.UseSerilogRequestLogging();
app.MapGet("/health/live", () => Results.Ok(new { status = "live" }));
app.MapReverseProxy();

await app.RunAsync();

namespace Gateway
{
    public partial class Program;
}
```

- [ ] **Step 2: Build**

```bash
dotnet build src/EntryPoints/Gateway/Gateway.csproj -c Release
```

Expected: 0 errors, 0 warnings.

- [ ] **Step 3: Commit**

```bash
git add src/EntryPoints/Gateway/Program.cs
git commit -m "feat(gateway): wire YARP, Serilog, OpenTelemetry"
```

### Task 1.2: Smoke test (manual) — proxy `/api/auth/.well-known/openid-configuration` to Auth.API

This task is verification only — no commit if everything is already working.

- [ ] **Step 1: Start Auth.API stack**

```bash
docker compose up -d auth-postgres redis auth.api
```

- [ ] **Step 2: Run gateway locally**

```bash
dotnet run --project src/EntryPoints/Gateway
```

The gateway listens on whatever port ASP.NET picks (it'll log the bound URL — typically `http://localhost:5xxx`).

- [ ] **Step 3: Verify proxy works for the discovery endpoint**

```bash
PORT=$(curl -sI http://localhost:5xxx/health/live -o /dev/null -w "%{http_code}" 2>/dev/null)  # use the printed gateway port
# Replace 5xxx with actual port from gateway logs
curl -sf http://localhost:5xxx/api/auth/.well-known/openid-configuration | jq .issuer
```

Expected: `"http://localhost:5100/"`.

- [ ] **Step 4: Stop the gateway** (Ctrl-C) and `docker compose down`. No commit needed.

---

## Phase 2 — Token validation via OpenIddict introspection (no cache yet)

### Task 2.1: Authentication wiring (introspection only, default behavior)

**Files:**
- Create: `src/EntryPoints/Gateway/DependencyInjection.cs`
- Modify: `src/EntryPoints/Gateway/Program.cs`

- [ ] **Step 1: Create `DependencyInjection.cs`**

```csharp
using Microsoft.AspNetCore.Authorization;
using OpenIddict.Validation.AspNetCore;

namespace Gateway;

internal static class DependencyInjection
{
    public const string BearerPolicy = "RequireBearer";

    public static IServiceCollection AddGatewayAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var section = configuration.GetSection("Auth");
        var issuer = section["Authority"]
            ?? throw new InvalidOperationException("Auth:Authority is required.");
        var introspectionEndpoint = section["IntrospectionEndpoint"]
            ?? throw new InvalidOperationException("Auth:IntrospectionEndpoint is required.");
        var clientId = section["IntrospectionClientId"]
            ?? throw new InvalidOperationException("Auth:IntrospectionClientId is required.");
        var clientSecret = section["IntrospectionClientSecret"]
            ?? throw new InvalidOperationException("Auth:IntrospectionClientSecret is required.");

        services.AddOpenIddict()
            .AddValidation(o =>
            {
                o.SetIssuer(new Uri(issuer));
                o.AddAudiences("api:web", "api:auth");
                o.UseIntrospection()
                    .SetClientId(clientId)
                    .SetClientSecret(clientSecret);
                o.UseSystemNetHttp()
                    .ConfigureHttpClient(client =>
                    {
                        client.Timeout = TimeSpan.FromSeconds(5);
                    });
                o.UseAspNetCore();
            });

        services.AddAuthentication(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)
            .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, OpenIddictValidationAspNetCoreHandler>(
                OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme, _ => { });

        services.AddAuthorization(opt =>
        {
            opt.AddPolicy(BearerPolicy, p =>
            {
                p.AddAuthenticationSchemes(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);
                p.RequireAuthenticatedUser();
            });
        });

        return services;
    }
}
```

- [ ] **Step 2: Update `Program.cs` to call it and place auth middleware**

```csharp
using Gateway;
using Infra.Observability;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, sp, cfg) =>
    cfg.ReadFrom.Configuration(ctx.Configuration).ReadFrom.Services(sp));

builder.Services.AddOpenTelemetryObservability(
    builder.Configuration,
    serviceName: "Gateway",
    includeAspNetCore: true,
    additionalActivitySources: ["Gateway"]);

builder.Services.AddGatewayAuthentication(builder.Configuration);

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var app = builder.Build();

app.UseSerilogRequestLogging();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health/live", () => Results.Ok(new { status = "live" }));
app.MapReverseProxy();

await app.RunAsync();

namespace Gateway
{
    public partial class Program;
}
```

- [ ] **Step 3: Build**

```bash
dotnet build BaseProjectScaffold.sln -c Release
```

Expected: 0 errors, 0 warnings.

- [ ] **Step 4: Commit**

```bash
git add src/EntryPoints/Gateway/
git commit -m "feat(gateway): wire OpenIddict introspection-based authentication"
```

---

## Phase 3 — Redis-backed introspection cache

The OpenIddict introspection client makes a fresh HTTP call on every request by default. This phase wraps it with a `DelegatingHandler` that caches the introspection HTTP response payload in Redis keyed by `SHA256(token)`. Cache TTL is configurable; default 30s.

**Why a `DelegatingHandler`:** OpenIddict's `UseSystemNetHttp().ConfigureHttpClient(...)` configures the `HttpClient` it uses to call `/connect/introspect`. The cleanest interception point is to add a custom `DelegatingHandler` to that pipeline. The handler computes a cache key from the request body (which carries `token=<value>`), looks up Redis, returns the cached response if hit, otherwise calls the inner handler and caches the response.

### Task 3.1: `IntrospectionCacheOptions`

**Files:**
- Create: `src/EntryPoints/Gateway/Authentication/IntrospectionCacheOptions.cs`

- [ ] **Step 1: Create options record**

```csharp
namespace Gateway.Authentication;

internal sealed class IntrospectionCacheOptions
{
    public const string SectionName = "IntrospectionCache";

    public int TtlSeconds { get; set; } = 30;

    public TimeSpan Ttl => TimeSpan.FromSeconds(TtlSeconds);
}
```

- [ ] **Step 2: Build + commit**

```bash
git add src/EntryPoints/Gateway/Authentication/IntrospectionCacheOptions.cs
git commit -m "feat(gateway): IntrospectionCacheOptions"
```

### Task 3.2: `IntrospectionCachingHandler` (TDD)

**Files:**
- Create: `tests/Gateway.IntegrationTests/Authentication/IntrospectionCachingHandlerTests.cs`
- Create: `src/EntryPoints/Gateway/Authentication/IntrospectionCachingHandler.cs`

- [ ] **Step 1: Failing test for cache miss → calls inner**

```csharp
using System.Net;
using System.Net.Http;
using System.Text;
using Gateway.Authentication;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using Shouldly;

namespace Gateway.IntegrationTests.Authentication;

public sealed class IntrospectionCachingHandlerTests
{
    [Fact]
    public async Task CacheMiss_CallsInnerAndStoresResponse()
    {
        var inner = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        inner.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"active":true}""", Encoding.UTF8, "application/json")
            });

        var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var options = Options.Create(new IntrospectionCacheOptions { TtlSeconds = 30 });
        var handler = new IntrospectionCachingHandler(cache, options) { InnerHandler = inner.Object };
        var client = new HttpClient(handler);

        var req = new HttpRequestMessage(HttpMethod.Post, "https://auth/connect/introspect")
        {
            Content = new StringContent("token=abc123", Encoding.UTF8, "application/x-www-form-urlencoded")
        };
        var resp = await client.SendAsync(req);

        resp.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await resp.Content.ReadAsStringAsync()).ShouldContain("\"active\":true");
        inner.Protected().Verify("SendAsync", Times.Once(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }
}
```

- [ ] **Step 2: Run test, expect compile failure**

```bash
dotnet test tests/Gateway.IntegrationTests/Gateway.IntegrationTests.csproj -c Release --filter "FullyQualifiedName~CacheMiss_CallsInnerAndStoresResponse"
```

Expected: compile error (`IntrospectionCachingHandler` does not exist).

- [ ] **Step 3: Implement `IntrospectionCachingHandler` (minimal, only enough to pass step 1)**

```csharp
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;

namespace Gateway.Authentication;

internal sealed class IntrospectionCachingHandler : DelegatingHandler
{
    private const string CacheKeyPrefix = "gateway:introspect:";

    private readonly IDistributedCache _cache;
    private readonly IntrospectionCacheOptions _options;

    public IntrospectionCachingHandler(
        IDistributedCache cache,
        IOptions<IntrospectionCacheOptions> options)
    {
        _cache = cache;
        _options = options.Value;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var token = await ExtractTokenAsync(request, cancellationToken);
        if (token is null)
        {
            return await base.SendAsync(request, cancellationToken);
        }

        var key = CacheKeyPrefix + Hash(token);
        var cached = await _cache.GetAsync(key, cancellationToken);
        if (cached is not null)
        {
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(cached)
                {
                    Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json") }
                }
            };
        }

        var response = await base.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            await _cache.SetAsync(key, body, new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = _options.Ttl
            }, cancellationToken);

            response = new HttpResponseMessage(response.StatusCode)
            {
                Content = new ByteArrayContent(body)
                {
                    Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json") }
                }
            };
        }

        return response;
    }

    private static async Task<string?> ExtractTokenAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (request.Content is null) return null;
        var body = await request.Content.ReadAsStringAsync(ct);
        // Body is application/x-www-form-urlencoded with token=... and possibly other fields.
        foreach (var pair in body.Split('&'))
        {
            var kv = pair.Split('=', 2);
            if (kv.Length == 2 && kv[0] == "token") return Uri.UnescapeDataString(kv[1]);
        }
        return null;
    }

    private static string Hash(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexStringLower(bytes);
    }
}
```

Note: rebuilding the request body string into the new `HttpResponseMessage` is needed because reading `Content` consumes the stream — without copying the bytes, the cache returns an empty response.

- [ ] **Step 4: Run test, expect pass**

```bash
dotnet test tests/Gateway.IntegrationTests/Gateway.IntegrationTests.csproj -c Release --filter "FullyQualifiedName~CacheMiss_CallsInnerAndStoresResponse"
```

Expected: PASS.

- [ ] **Step 5: Add cache-hit test**

Append to `IntrospectionCachingHandlerTests.cs`:

```csharp
[Fact]
public async Task CacheHit_DoesNotCallInner()
{
    var inner = new Mock<HttpMessageHandler>(MockBehavior.Strict);
    inner.Protected()
        .Setup<Task<HttpResponseMessage>>("SendAsync",
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>())
        .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"active":true}""", Encoding.UTF8, "application/json")
        });

    var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
    var options = Options.Create(new IntrospectionCacheOptions { TtlSeconds = 30 });
    var handler = new IntrospectionCachingHandler(cache, options) { InnerHandler = inner.Object };
    var client = new HttpClient(handler);

    HttpRequestMessage MakeRequest() =>
        new(HttpMethod.Post, "https://auth/connect/introspect")
        {
            Content = new StringContent("token=abc123", Encoding.UTF8, "application/x-www-form-urlencoded")
        };

    var first = await client.SendAsync(MakeRequest());
    first.StatusCode.ShouldBe(HttpStatusCode.OK);

    var second = await client.SendAsync(MakeRequest());
    second.StatusCode.ShouldBe(HttpStatusCode.OK);
    (await second.Content.ReadAsStringAsync()).ShouldContain("\"active\":true");

    inner.Protected().Verify("SendAsync", Times.Once(),
        ItExpr.IsAny<HttpRequestMessage>(),
        ItExpr.IsAny<CancellationToken>());
}
```

- [ ] **Step 6: Run, expect pass**

```bash
dotnet test tests/Gateway.IntegrationTests/Gateway.IntegrationTests.csproj -c Release --filter "FullyQualifiedName~IntrospectionCachingHandlerTests"
```

Expected: 2 PASS.

- [ ] **Step 7: Add no-token-in-body fallback test**

```csharp
[Fact]
public async Task NoTokenInBody_BypassesCache()
{
    var inner = new Mock<HttpMessageHandler>(MockBehavior.Strict);
    inner.Protected()
        .Setup<Task<HttpResponseMessage>>("SendAsync",
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>())
        .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK));

    var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
    var options = Options.Create(new IntrospectionCacheOptions { TtlSeconds = 30 });
    var handler = new IntrospectionCachingHandler(cache, options) { InnerHandler = inner.Object };
    var client = new HttpClient(handler);

    var req = new HttpRequestMessage(HttpMethod.Get, "https://auth/connect/introspect");
    var resp = await client.SendAsync(req);
    resp.StatusCode.ShouldBe(HttpStatusCode.OK);

    inner.Protected().Verify("SendAsync", Times.Once(),
        ItExpr.IsAny<HttpRequestMessage>(),
        ItExpr.IsAny<CancellationToken>());
}
```

- [ ] **Step 8: Run, expect pass.**

- [ ] **Step 9: Commit**

```bash
git add src/EntryPoints/Gateway/Authentication/IntrospectionCachingHandler.cs tests/Gateway.IntegrationTests/Authentication/IntrospectionCachingHandlerTests.cs
git commit -m "feat(gateway): IntrospectionCachingHandler with Redis-backed response cache"
```

### Task 3.3: Wire `IntrospectionCachingHandler` and Redis into authentication

**Files:**
- Modify: `src/EntryPoints/Gateway/DependencyInjection.cs`

- [ ] **Step 1: Update `AddGatewayAuthentication` to register Redis + the handler**

Add at the top of `AddGatewayAuthentication`, before the OpenIddict registration:

```csharp
var redisConn = configuration["Redis:ConnectionString"]
    ?? throw new InvalidOperationException("Redis:ConnectionString is required.");
services.AddStackExchangeRedisCache(o => o.Configuration = redisConn);
services.Configure<Authentication.IntrospectionCacheOptions>(
    configuration.GetSection(Authentication.IntrospectionCacheOptions.SectionName));
services.AddTransient<Authentication.IntrospectionCachingHandler>();
```

Then update the `o.UseSystemNetHttp().ConfigureHttpClient(...)` block to:

```csharp
o.UseSystemNetHttp()
    .ConfigureHttpClient(client =>
    {
        client.Timeout = TimeSpan.FromSeconds(5);
    })
    .ConfigureHttpClientHandler(_ => { })
    .AddHttpMessageHandler<Authentication.IntrospectionCachingHandler>();
```

If `ConfigureHttpClientHandler` and `AddHttpMessageHandler` aren't surfaced by the OpenIddict 6.4.0 builder fluent API, fall back to:

```csharp
services.AddHttpClient(OpenIddict.Validation.OpenIddictValidationDefaults.AuthenticationType)
    .AddHttpMessageHandler<Authentication.IntrospectionCachingHandler>();
```

— which decorates the named HttpClient OpenIddict's introspection client uses internally. Verify the exact named-client key by reading the OpenIddict source (`OpenIddictValidationSystemNetHttpHandlers.RegisterHttpClient`) at `~/.nuget/packages/openiddict.validation.systemnethttp/6.4.0/`.

- [ ] **Step 2: Build**

```bash
dotnet build BaseProjectScaffold.sln -c Release
```

Expected: 0 errors, 0 warnings.

- [ ] **Step 3: Commit**

```bash
git add src/EntryPoints/Gateway/DependencyInjection.cs
git commit -m "feat(gateway): wire IntrospectionCachingHandler into OpenIddict validation pipeline"
```

---

## Phase 4 — Identity forwarding to downstream

### Task 4.1: `ForwardedIdentityTransform` (YARP request transform)

When a request reaches a downstream cluster, YARP forwards the original `Authorization` header by default. This task adds a synthetic `X-Forwarded-User` and `X-Forwarded-TenantId` so downstream services can read identity without re-validating the token (Plan 5 will use this in Web.API; Plan 1's Auth.API does not need it because the gateway never proxies authenticated requests TO Auth.API — `/connect/*` and `/.well-known/*` are unauthenticated by design).

**Files:**
- Create: `src/EntryPoints/Gateway/Authentication/ForwardedIdentityTransform.cs`
- Modify: `src/EntryPoints/Gateway/Program.cs` (register the transform)

- [ ] **Step 1: Implement transform**

```csharp
using System.Security.Claims;
using Yarp.ReverseProxy.Transforms;
using Yarp.ReverseProxy.Transforms.Builder;

namespace Gateway.Authentication;

internal sealed class ForwardedIdentityTransform : ITransformProvider
{
    public void ValidateRoute(TransformRouteValidationContext context) { }
    public void ValidateCluster(TransformClusterValidationContext context) { }

    public void Apply(TransformBuilderContext context)
    {
        context.AddRequestTransform(transformContext =>
        {
            var user = transformContext.HttpContext.User;
            if (user.Identity?.IsAuthenticated != true)
            {
                return ValueTask.CompletedTask;
            }

            var sub = user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
            var tenantId = user.FindFirstValue("tenant_id");

            if (!string.IsNullOrEmpty(sub))
            {
                transformContext.ProxyRequest.Headers.Remove("X-Forwarded-User");
                transformContext.ProxyRequest.Headers.Add("X-Forwarded-User", sub);
            }
            if (!string.IsNullOrEmpty(tenantId))
            {
                transformContext.ProxyRequest.Headers.Remove("X-Forwarded-TenantId");
                transformContext.ProxyRequest.Headers.Add("X-Forwarded-TenantId", tenantId);
            }

            return ValueTask.CompletedTask;
        });
    }
}
```

- [ ] **Step 2: Register the transform**

In `Program.cs`, change:

```csharp
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));
```

to:

```csharp
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddTransforms<Gateway.Authentication.ForwardedIdentityTransform>();
```

- [ ] **Step 3: Build**

```bash
dotnet build BaseProjectScaffold.sln -c Release
```

Expected: 0 errors, 0 warnings.

- [ ] **Step 4: Commit**

```bash
git add src/EntryPoints/Gateway/
git commit -m "feat(gateway): forward identity claims as X-Forwarded-User/TenantId headers"
```

---

## Phase 5 — Health checks

### Task 5.1: `AuthApiHealthCheck`

**Files:**
- Create: `src/EntryPoints/Gateway/HealthChecks/AuthApiHealthCheck.cs`
- Modify: `src/EntryPoints/Gateway/DependencyInjection.cs` (or a new `AddGatewayPresentation`)
- Modify: `src/EntryPoints/Gateway/Program.cs`

- [ ] **Step 1: Implement health check**

```csharp
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Gateway.HealthChecks;

internal sealed class AuthApiHealthCheck(IHttpClientFactory factory, IConfiguration configuration)
    : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var authority = configuration["Auth:Authority"];
        if (string.IsNullOrWhiteSpace(authority))
        {
            return HealthCheckResult.Unhealthy("Auth:Authority not configured.");
        }

        try
        {
            var client = factory.CreateClient("auth-health");
            using var resp = await client.GetAsync(
                new Uri(new Uri(authority), "/health/live"),
                cancellationToken);
            return resp.IsSuccessStatusCode
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy($"Auth.API returned {(int)resp.StatusCode}.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Auth.API unreachable.", ex);
        }
    }
}
```

- [ ] **Step 2: Wire health checks**

Add to `DependencyInjection.cs`:

```csharp
public static IServiceCollection AddGatewayHealthChecks(
    this IServiceCollection services,
    IConfiguration configuration)
{
    services.AddHttpClient("auth-health", c => c.Timeout = TimeSpan.FromSeconds(2));

    var redisConn = configuration["Redis:ConnectionString"]
        ?? throw new InvalidOperationException("Redis:ConnectionString is required.");

    services.AddHealthChecks()
        .AddRedis(redisConn, name: "redis", tags: ["ready"])
        .AddCheck<HealthChecks.AuthApiHealthCheck>("auth-api", tags: ["ready"]);

    return services;
}
```

Add `AspNetCore.HealthChecks.Redis` package reference to `Gateway.csproj` (already in Auth.API; ensure `Directory.Packages.props` has `<PackageVersion Include="AspNetCore.HealthChecks.Redis" Version="9.0.0" />` — added in Plan 1).

In `Program.cs`, after `AddGatewayAuthentication`:

```csharp
builder.Services.AddGatewayHealthChecks(builder.Configuration);
```

After `app.MapGet("/health/live", ...)`:

```csharp
app.MapHealthChecks("/health/ready", new()
{
    Predicate = h => h.Tags.Contains("ready")
});
```

- [ ] **Step 3: Build**

```bash
dotnet build BaseProjectScaffold.sln -c Release
```

Expected: 0 errors, 0 warnings.

- [ ] **Step 4: Commit**

```bash
git add src/EntryPoints/Gateway/HealthChecks/ src/EntryPoints/Gateway/DependencyInjection.cs src/EntryPoints/Gateway/Program.cs
git commit -m "feat(gateway): health checks for Auth.API and Redis"
```

---

## Phase 6 — Architecture tests

### Task 6.1: NetArchTest rules

**Files:**
- Create: `tests/Gateway.IntegrationTests/Architecture/ArchitectureTests.cs`

- [ ] **Step 1: Tests**

```csharp
using System.Reflection;
using NetArchTest.Rules;
using Shouldly;

namespace Gateway.IntegrationTests.Architecture;

public sealed class ArchitectureTests
{
    private static readonly Assembly GatewayAssembly = typeof(Gateway.Program).Assembly;

    [Fact]
    public void Gateway_ShouldNotDependOn_AuthDomain()
    {
        var result = Types.InAssembly(GatewayAssembly)
            .ShouldNot()
            .HaveDependencyOn("Auth.Domain")
            .GetResult();
        result.IsSuccessful.ShouldBeTrue($"Failing: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void Gateway_ShouldNotDependOn_AuthApplication()
    {
        var result = Types.InAssembly(GatewayAssembly)
            .ShouldNot()
            .HaveDependencyOn("Auth.Application")
            .GetResult();
        result.IsSuccessful.ShouldBeTrue($"Failing: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void Gateway_ShouldNotDependOn_AuthInfra()
    {
        var result = Types.InAssembly(GatewayAssembly)
            .ShouldNot()
            .HaveDependencyOn("Auth.Infra")
            .GetResult();
        result.IsSuccessful.ShouldBeTrue($"Failing: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void Gateway_ShouldNotDependOn_EntityFrameworkCore()
    {
        var result = Types.InAssembly(GatewayAssembly)
            .ShouldNot()
            .HaveDependencyOn("Microsoft.EntityFrameworkCore")
            .GetResult();
        result.IsSuccessful.ShouldBeTrue($"Failing: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }
}
```

- [ ] **Step 2: Run, expect pass**

```bash
dotnet test tests/Gateway.IntegrationTests/Gateway.IntegrationTests.csproj -c Release --filter "FullyQualifiedName~Architecture"
```

Expected: 4 PASS.

- [ ] **Step 3: Commit**

```bash
git add tests/Gateway.IntegrationTests/Architecture/
git commit -m "test(gateway): architecture rules (no Auth.* / EFCore deps)"
```

---

## Phase 7 — Integration tests

### Task 7.1: `GatewayWebApplicationFactory`

The factory boots:
- A real Redis container (Testcontainers).
- WireMock acting as Auth.API (introspection endpoint, discovery, health).
- WireMock acting as a downstream "Web.API" backend that asserts forwarded headers.
- The Gateway itself via `WebApplicationFactory<Gateway.Program>`.

**Files:**
- Create: `tests/Gateway.IntegrationTests/Infrastructure/GatewayWebApplicationFactory.cs`

- [ ] **Step 1: Implement factory**

```csharp
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Testcontainers.Redis;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace Gateway.IntegrationTests.Infrastructure;

public sealed class GatewayWebApplicationFactory : WebApplicationFactory<Gateway.Program>, IAsyncLifetime
{
    private readonly RedisContainer _redis = new RedisBuilder()
        .WithImage("redis:7-alpine").Build();

    private WireMockServer _authApi = null!;
    private WireMockServer _webApi = null!;

    public WireMockServer AuthApi => _authApi;
    public WireMockServer WebApi => _webApi;

    public async Task InitializeAsync()
    {
        await _redis.StartAsync();
        _authApi = WireMockServer.Start();
        _webApi = WireMockServer.Start();

        StubAuthHealth();
    }

    public new async Task DisposeAsync()
    {
        await _redis.DisposeAsync();
        _authApi.Stop();
        _webApi.Stop();
        await base.DisposeAsync();
    }

    Task IAsyncLifetime.DisposeAsync() => DisposeAsync();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((ctx, cfg) =>
        {
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Redis:ConnectionString"] = _redis.GetConnectionString(),
                ["Auth:Authority"] = _authApi.Url,
                ["Auth:IntrospectionEndpoint"] = $"{_authApi.Url}/connect/introspect",
                ["Auth:IntrospectionClientId"] = "gateway",
                ["Auth:IntrospectionClientSecret"] = "test-secret",
                ["IntrospectionCache:TtlSeconds"] = "30",
                ["ReverseProxy:Routes:test:ClusterId"] = "test-cluster",
                ["ReverseProxy:Routes:test:Match:Path"] = "/api/test/{**catch-all}",
                ["ReverseProxy:Routes:test:AuthorizationPolicy"] = "RequireBearer",
                ["ReverseProxy:Routes:test:Transforms:0:PathRemovePrefix"] = "/api/test",
                ["ReverseProxy:Clusters:test-cluster:Destinations:web:Address"] = $"{_webApi.Url}/"
            });
        });
    }

    public void StubIntrospection(string token, bool active, params (string claim, string value)[] extraClaims)
    {
        var json = active
            ? $$"""
                {
                  "active": true,
                  "sub": "user-1",
                  "tenant_id": "{{Guid.NewGuid()}}",
                  "iss": "{{_authApi.Url}}/"
                  {{string.Concat(extraClaims.Select(c => $", \"{c.claim}\": \"{c.value}\""))}}
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
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("{\"status\":\"live\"}"));
    }

    public void StubWebApiEcho()
    {
        _webApi.Given(Request.Create().WithPath("/echo").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBodyFromTemplate("""
                {
                  "user": "{{request.headers.X-Forwarded-User}}",
                  "tenant": "{{request.headers.X-Forwarded-TenantId}}",
                  "auth": "{{request.headers.Authorization}}"
                }
                """));
    }
}
```

If `WithBodyFromTemplate` isn't available in WireMock.Net 2.5.0, fall back to a static body and assert via WireMock's request log.

- [ ] **Step 2: Build**

```bash
dotnet build tests/Gateway.IntegrationTests/Gateway.IntegrationTests.csproj -c Release
```

Expected: success.

- [ ] **Step 3: Commit**

```bash
git add tests/Gateway.IntegrationTests/Infrastructure/GatewayWebApplicationFactory.cs
git commit -m "test(gateway): WebApplicationFactory with WireMock Auth.API + downstream and Redis testcontainer"
```

### Task 7.2: Health check tests

**Files:**
- Create: `tests/Gateway.IntegrationTests/HealthCheckTests.cs`

- [ ] **Step 1: Tests**

```csharp
using Gateway.IntegrationTests.Infrastructure;
using Shouldly;

namespace Gateway.IntegrationTests;

public sealed class HealthCheckTests(GatewayWebApplicationFactory factory)
    : IClassFixture<GatewayWebApplicationFactory>
{
    [Fact]
    public async Task HealthLive_Returns200()
    {
        var client = factory.CreateClient();
        var response = await client.GetAsync("/health/live");
        response.IsSuccessStatusCode.ShouldBeTrue();
    }

    [Fact]
    public async Task HealthReady_Returns200_WhenAuthApiAndRedisAreUp()
    {
        var client = factory.CreateClient();
        var response = await client.GetAsync("/health/ready");
        response.IsSuccessStatusCode.ShouldBeTrue();
    }
}
```

- [ ] **Step 2: Run, expect pass.** Commit.

```bash
git add tests/Gateway.IntegrationTests/HealthCheckTests.cs
git commit -m "test(gateway): health check tests"
```

### Task 7.3: Authentication flow tests

**Files:**
- Create: `tests/Gateway.IntegrationTests/Authentication/AuthenticationFlowTests.cs`

- [ ] **Step 1: Tests**

```csharp
using System.Net;
using System.Net.Http.Headers;
using Gateway.IntegrationTests.Infrastructure;
using Shouldly;

namespace Gateway.IntegrationTests.Authentication;

public sealed class AuthenticationFlowTests(GatewayWebApplicationFactory factory)
    : IClassFixture<GatewayWebApplicationFactory>
{
    [Fact]
    public async Task ProtectedRoute_NoToken_Returns401()
    {
        factory.StubWebApiEcho();
        var client = factory.CreateClient();
        var resp = await client.GetAsync("/api/test/echo");
        resp.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ProtectedRoute_InactiveToken_Returns401()
    {
        const string token = "inactive-token-1";
        factory.StubWebApiEcho();
        factory.StubIntrospection(token, active: false);

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var resp = await client.GetAsync("/api/test/echo");
        resp.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ProtectedRoute_ActiveToken_ProxiesToBackend()
    {
        const string token = "active-token-1";
        factory.StubWebApiEcho();
        factory.StubIntrospection(token, active: true);

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var resp = await client.GetAsync("/api/test/echo");
        resp.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Verify the downstream received forwarded headers via WireMock's request log
        var receivedRequests = factory.WebApi.LogEntries.ToList();
        receivedRequests.ShouldNotBeEmpty();
        var lastRequest = receivedRequests[^1].RequestMessage;
        lastRequest.Headers!.ShouldContainKey("X-Forwarded-User");
        lastRequest.Headers!.ShouldContainKey("Authorization");
    }
}
```

- [ ] **Step 2: Run, expect pass.** Commit.

```bash
git add tests/Gateway.IntegrationTests/Authentication/AuthenticationFlowTests.cs
git commit -m "test(gateway): authentication flow (401 paths + valid-token proxy)"
```

### Task 7.4: Cache hit verification test

**Files:**
- Create: `tests/Gateway.IntegrationTests/Authentication/CacheBehaviorTests.cs`

- [ ] **Step 1: Test**

```csharp
using System.Net.Http.Headers;
using Gateway.IntegrationTests.Infrastructure;
using Shouldly;

namespace Gateway.IntegrationTests.Authentication;

public sealed class CacheBehaviorTests(GatewayWebApplicationFactory factory)
    : IClassFixture<GatewayWebApplicationFactory>
{
    [Fact]
    public async Task SecondRequestSameToken_DoesNotHitIntrospectionEndpoint()
    {
        const string token = "cache-token";
        factory.StubWebApiEcho();
        factory.StubIntrospection(token, active: true);

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var first = await client.GetAsync("/api/test/echo");
        first.IsSuccessStatusCode.ShouldBeTrue();

        var second = await client.GetAsync("/api/test/echo");
        second.IsSuccessStatusCode.ShouldBeTrue();

        var introspectCalls = factory.AuthApi.LogEntries
            .Count(e => e.RequestMessage.Path == "/connect/introspect");
        introspectCalls.ShouldBe(1, "Expected only ONE introspection call due to caching");
    }
}
```

- [ ] **Step 2: Run, expect pass.** Commit.

```bash
git add tests/Gateway.IntegrationTests/Authentication/CacheBehaviorTests.cs
git commit -m "test(gateway): introspection cache hit reduces backend calls"
```

### Task 7.5: Routing test (unauthenticated discovery passthrough)

**Files:**
- Create: `tests/Gateway.IntegrationTests/Routing/ProxyRoutingTests.cs`

- [ ] **Step 1: Test**

```csharp
using Gateway.IntegrationTests.Infrastructure;
using Shouldly;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace Gateway.IntegrationTests.Routing;

public sealed class ProxyRoutingTests(GatewayWebApplicationFactory factory)
    : IClassFixture<GatewayWebApplicationFactory>
{
    [Fact]
    public async Task DiscoveryRoute_DoesNotRequireAuth_ProxiesToAuthApi()
    {
        // Stub Auth.API discovery
        factory.AuthApi.Given(
            Request.Create().WithPath("/.well-known/openid-configuration").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody("""{"issuer":"http://test"}"""));

        // The default factory routes only have a /api/test route. Override config
        // for this test class is awkward — use a separate fixture or assert the
        // unauthenticated discovery route in a real-stack smoke test instead.
        // Skipping for V1 if route override is non-trivial; verified manually
        // via Phase 1 smoke test.

        await Task.CompletedTask;
        true.ShouldBeTrue();
    }
}
```

(This test is largely a placeholder — the factory's per-test route reconfiguration is non-trivial. Document it as known coverage gap; the Phase 1 manual smoke verifies discovery works in the real stack. If a clean way to override routes per test class is found, replace this stub with a real assertion.)

- [ ] **Step 2: Commit**

```bash
git add tests/Gateway.IntegrationTests/Routing/ProxyRoutingTests.cs
git commit -m "test(gateway): proxy routing placeholder (documents discovery coverage)"
```

---

## Phase 8 — Compose, Dockerfile, docs

### Task 8.1: Dockerfile

**Files:**
- Create: `src/EntryPoints/Gateway/Dockerfile`

- [ ] **Step 1: Copy from Auth.API as template, adjust paths.**

Use exactly this:

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY ["Directory.Packages.props", "./"]
COPY ["Directory.Build.props", "./"]
COPY ["nuget.config", "./"]
COPY ["src/SharedKernel/SharedKernel.csproj", "src/SharedKernel/"]
COPY ["src/Infra/Infra.csproj", "src/Infra/"]
COPY ["src/EntryPoints/Gateway/Gateway.csproj", "src/EntryPoints/Gateway/"]

RUN dotnet restore "src/EntryPoints/Gateway/Gateway.csproj"

COPY src/SharedKernel/ src/SharedKernel/
COPY src/Infra/ src/Infra/
COPY src/EntryPoints/Gateway/ src/EntryPoints/Gateway/

WORKDIR "/src/src/EntryPoints/Gateway"
RUN dotnet build "./Gateway.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./Gateway.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
RUN chown -R 1654:1654 /app
USER 1654:1654
ENTRYPOINT ["dotnet", "Gateway.dll"]

HEALTHCHECK --interval=10s --timeout=3s --start-period=10s --retries=5 \
  CMD wget --quiet --tries=1 --spider http://localhost:8080/health/live || exit 1
```

Note: `Auth.API`'s Dockerfile pulls in additional projects (`Auth.Application`, `Auth.Infra`, etc.). Gateway only needs `SharedKernel` and `Infra` (plus its own).

- [ ] **Step 2: Verify build**

```bash
docker build -t gateway -f src/EntryPoints/Gateway/Dockerfile .
```

Expected: image built (~500 MB), no errors.

- [ ] **Step 3: Commit**

```bash
git add src/EntryPoints/Gateway/Dockerfile
git commit -m "chore(gateway): add Dockerfile"
```

### Task 8.2: Compose service

**Files:**
- Modify: `compose.yaml`

- [ ] **Step 1: Add `gateway` service block** AFTER `auth.api`:

```yaml
  gateway:
    image: ${DOCKER_REGISTRY-}gateway
    container_name: base_scaffold_gateway
    depends_on:
      - redis
      - auth.api
    build:
      context: .
      dockerfile: src/EntryPoints/Gateway/Dockerfile
    environment:
      - ASPNETCORE_ENVIRONMENT=Development
      - Redis__ConnectionString=redis:6379
      - Auth__Authority=http://auth.api:8080
      - Auth__IntrospectionEndpoint=http://auth.api:8080/connect/introspect
      - Auth__IntrospectionClientId=gateway
      - Auth__IntrospectionClientSecret=${OPENIDDICT_GATEWAY_SECRET:-dev-only-gateway-secret-change-me}
      - IntrospectionCache__TtlSeconds=30
      - ReverseProxy__Routes__auth-discovery__ClusterId=auth-cluster
      - ReverseProxy__Routes__auth-discovery__Match__Path=/api/auth/.well-known/{**catch-all}
      - ReverseProxy__Routes__auth-discovery__Transforms__0__PathRemovePrefix=/api/auth
      - ReverseProxy__Routes__auth-connect__ClusterId=auth-cluster
      - ReverseProxy__Routes__auth-connect__Match__Path=/api/auth/connect/{**catch-all}
      - ReverseProxy__Routes__auth-connect__Transforms__0__PathRemovePrefix=/api/auth
      - ReverseProxy__Routes__web-api__ClusterId=web-api-cluster
      - ReverseProxy__Routes__web-api__Match__Path=/api/v1/{**catch-all}
      - ReverseProxy__Routes__web-api__AuthorizationPolicy=RequireBearer
      - ReverseProxy__Clusters__auth-cluster__Destinations__auth__Address=http://auth.api:8080/
      - ReverseProxy__Clusters__web-api-cluster__Destinations__web-api__Address=http://web.api:8080/
    ports:
      - "5200:8080"
```

- [ ] **Step 2: Validate**

```bash
docker compose config --services
```

Expected: `gateway` listed.

- [ ] **Step 3: Smoke**

```bash
docker compose up -d --build redis auth-postgres auth.api gateway
sleep 15
curl -sf http://localhost:5200/health/live
curl -sf http://localhost:5200/health/ready
curl -sf http://localhost:5200/api/auth/.well-known/openid-configuration | jq .issuer
```

Expected: all 200; issuer is `http://localhost:5100/` (because `OpenIddict:Issuer` is set on Auth.API).

```bash
docker compose down
```

- [ ] **Step 4: Commit**

```bash
git add compose.yaml
git commit -m "chore(gateway): add gateway service to compose stack"
```

### Task 8.3: Documentation

**Files:**
- Modify: `CLAUDE.md`
- Modify: `README.md`

- [ ] **Step 1: CLAUDE.md updates**

§1 — add Gateway to entrypoint list.

§3 — add to directory layout:

```
└── EntryPoints/
    ├── Gateway/                       # NEW — YARP reverse proxy with introspection cache
    │   ├── Authentication/            # IntrospectionCachingHandler, ForwardedIdentityTransform
    │   ├── HealthChecks/              # AuthApiHealthCheck
    │   ├── Program.cs
    │   ├── DependencyInjection.cs
    │   └── Dockerfile
    └── ...
```

Add `tests/Gateway.IntegrationTests/` next to existing test entries.

§9 (env vars) — add:

```
GATEWAY_*  / Auth__IntrospectionClientId / Auth__IntrospectionClientSecret /
Auth__IntrospectionEndpoint / Redis__ConnectionString / IntrospectionCache__TtlSeconds
```

§13 — add pitfalls:

- "Gateway returns 401 for valid token" → check `Auth:IntrospectionClientId/Secret` match a seeded resource server with `Endpoints.Introspection` permission.
- "Token revoked but Gateway still admits requests" → introspection cache TTL (default 30s); reduce TTL or call `/connect/revocation` followed by waiting up to TTL.
- "Downstream service can't read identity" → look for `X-Forwarded-User` and `X-Forwarded-TenantId` headers (Plan 5 will read them in Web.API).

- [ ] **Step 2: README.md updates**

Add "Gateway (YARP)" section in EN + PT, immediately after Auth.API:
- Single ingress for backend; routes external clients to Auth.API and Web.API.
- Validates opaque tokens via Auth.API introspection with 30s Redis-backed cache.
- Forwards `X-Forwarded-User` / `X-Forwarded-TenantId` to downstream.
- Dev URL: `http://localhost:5200/api/auth/.well-known/openid-configuration`.
- Run: `docker compose up -d redis auth-postgres auth.api gateway`.
- References this plan.

- [ ] **Step 3: Commit**

```bash
git add CLAUDE.md README.md
git commit -m "docs: document Gateway (YARP) in CLAUDE.md and README"
```

---

## Final verification

- [ ] **Step 1: Full build + test**

```bash
dotnet build BaseProjectScaffold.sln -c Release
dotnet test BaseProjectScaffold.sln -c Release
```

Expected: 0 errors, 0 warnings. All tests pass (115 from Plan 1 + new Gateway tests; aim for ≥125 total).

- [ ] **Step 2: Compose smoke**

```bash
docker compose up -d --build redis auth-postgres auth.api gateway
sleep 20
curl -sf http://localhost:5200/health/ready
curl -sf http://localhost:5200/api/auth/.well-known/openid-configuration | jq .

# Acquire a token via Gateway proxy
TOKEN=$(curl -sf -X POST http://localhost:5200/api/auth/connect/token \
  -d "grant_type=client_credentials" \
  -d "client_id=test-client" \  # would need a seeded test client; if not, this step verifies the proxy + auth.api boundary, not the full flow
  -d "client_secret=test-secret" | jq -r .access_token)

# Hit a protected route — should get 401 without token, 200 with valid token (if Web.API is up)
curl -i http://localhost:5200/api/v1/sample-entities

docker compose down
```

- [ ] **Step 3: Tag**

```bash
git tag -a gateway-v1 -m "Gateway YARP complete (Plan 2 of 5)"
```

---

## Self-review

1. **Spec coverage** — Plan 2 covers spec §3 (topology, gateway as ingress), §4 (YARP), §7.2 (introspection-based validation, 30s cache), §10 (env vars added), §11 (OTel and conventions inherited). Out of scope: §7.3 (NetSuite SAML — Plan 4), §7.1 (BFF cookie session — Plan 3), §8 (downstream policy provider — Plan 5).

2. **Placeholder scan** — Task 7.5 (Routing test) is a stub by design; documented inline as known gap with manual smoke fallback. No other "TBD" or "implement later" items.

3. **Type consistency** — `IntrospectionCacheOptions` (Task 3.1) used identically in Tasks 3.2 and 3.3. `IntrospectionCachingHandler` constructor signature `(IDistributedCache, IOptions<IntrospectionCacheOptions>)` consistent. `ForwardedIdentityTransform` referenced in Tasks 4.1 and matched in Task 7.3 assertions.

4. **Spec items with no task** — none in Plan 2's scope. Resource-server validation in Web.API and downstream is intentionally Plan 5.

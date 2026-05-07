# Web.API Integration with Auth.API Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the existing `Web.API`'s home-grown JWT bearer authentication with `OpenIddict.Validation.AspNetCore` introspection-based validation against `Auth.API`. Add a permission-based `IAuthorizationPolicyProvider` so endpoints declare `RequireAuthorization("permission:users.read")`. Propagate tenant identity from the `X-Forwarded-TenantId` header set by the Gateway. Preserve existing endpoint shapes; only the auth pipeline changes. Move Web.API behind the Gateway in compose so external clients never reach Web.API directly.

**Architecture:** Web.API drops `Microsoft.AspNetCore.Authentication.JwtBearer` (and `JWT_SECRET` configuration) and adds `OpenIddict.Validation.AspNetCore` + `OpenIddict.Validation.SystemNetHttp` configured to introspect against `Auth.API/connect/introspect` using the seeded `web-api` resource server credentials. A small `TenantContextMiddleware` reads `X-Forwarded-TenantId` (set by the Gateway's `ForwardedIdentityTransform`) and exposes it via the existing `IUserContext` interface (extended with `TenantId`). Handlers can inject `IUserContext.TenantId` for tenant-scoped queries against `app_db` (the existing scaffold's business DB). The Sample feature gets `TenantId` columns to demonstrate the pattern; **business-side tenant scoping is illustrated, not enforced exhaustively** (each forked project chooses how deep to wire it).

**Tech Stack:** .NET 10, `OpenIddict.Validation.AspNetCore 6.4.0`, `OpenIddict.Validation.SystemNetHttp 6.4.0`, `Microsoft.Extensions.Caching.StackExchangeRedis 10.0.7` (already present), the existing scaffold's `Web.API`, `Application`, `Infra`, `Domain`, `SharedKernel`.

**Out of scope (deferred):**
- Wiring `IUserContext.TenantId` into every existing query (V1: just the Sample feature, demonstrates pattern).
- Cross-cutting tenant guard in `ApplicationDbContext` (the existing business DbContext) — pattern from `AuthDbContext` could be ported, follow-up.
- Removing `JWT_SECRET` from Worker / CronJobs / Web.Blazor (Web.Blazor already removed in Plan 3; Worker and CronJobs stay JWT-less since they don't accept inbound auth — they use M2M tokens to call other services if needed).
- mTLS between Gateway → Web.API (V2 hardening).

---

## Context for the implementer

- Branch: `feature/web-api-integration`, branched from `feature/netsuite-saml` HEAD.
- Web.API today: read `src/EntryPoints/Web.API/Program.cs`. It calls `AddInfrastructure` from Plan 1 days, which configures JWT bearer with `JWT_SECRET`. The flow:
  - `Infra.DependencyInjection.AddInfrastructure(...)` registers JWT bearer with the secret.
  - `IUserContext` (in `src/Application/Abstractions/Authentication/IUserContext.cs`) is the existing user-context abstraction. Read its current shape; extend with `TenantId`.
- The Gateway already sets `X-Forwarded-User` and `X-Forwarded-TenantId` (Plan 2 Task 4.1).
- Auth.API tokens are opaque. Introspection clients: `web-api` / `dev-only-web-api-secret-change-me` (Plan 1 seed).
- Existing tests in `tests/Web.API.IntegrationTests/Infrastructure/CustomWebApplicationFactory.cs` mint JWTs with the `JWT_SECRET`. These need to be replaced with introspection-based stubs (similar to the Gateway tests' WireMock pattern).

---

## File structure

```
src/Infra/
└── Authentication/
    ├── (existing JwtBearer wiring removed/refactored)
    └── IntrospectionAuthenticationExtensions.cs    # NEW
src/Application/Abstractions/Authentication/
└── IUserContext.cs                                  # MODIFY — add TenantId

src/Web.API/
├── Authorization/
│   ├── PermissionRequirement.cs                     # mirror Auth.API/Authorization
│   ├── PermissionAuthorizationHandler.cs
│   └── PermissionPolicyProvider.cs
├── Middleware/
│   ├── TenantContextMiddleware.cs                   # reads X-Forwarded-TenantId
│   └── (existing middleware untouched)
├── DependencyInjection.cs                           # MODIFY — add introspection auth + policy provider
└── Program.cs                                       # MODIFY — replace JWT with introspection, register middleware

src/Domain/
└── SampleEntities/
    └── SampleEntity.cs                              # MODIFY — add TenantId property
src/Infra/
└── Database/
    └── Migrations/                                   # NEW migration: add tenant_id to sample_entities
src/Application/
└── SampleEntities/                                  # MODIFY — handlers filter by TenantId

tests/Web.API.IntegrationTests/
├── Infrastructure/
│   └── CustomWebApplicationFactory.cs               # MODIFY — replace JWT minter with WireMock introspection stub
├── Authentication/
│   ├── IntrospectionAuthTests.cs                    # NEW
│   └── PermissionPolicyTests.cs                     # NEW
└── Endpoints/
    └── SampleEntityEndpointTests.cs                 # MODIFY — use introspection-based fake tokens
```

---

## Phase 0 — IUserContext extension

### Task 0.1: Add `TenantId` to `IUserContext`

**Files:**
- Modify: `src/Application/Abstractions/Authentication/IUserContext.cs`
- Modify: `src/Infra/Authentication/UserContext.cs` (the impl)

Read existing `IUserContext`:

Currently:
```csharp
public interface IUserContext
{
    Guid UserId { get; }
    bool IsAuthenticated { get; }
}
```

Modify:
```csharp
public interface IUserContext
{
    Guid UserId { get; }
    Guid? TenantId { get; }
    bool IsAuthenticated { get; }
}
```

Modify `UserContext` impl to read `tenant_id` claim from `HttpContext.User`. If the claim is absent or unparseable, return `null`.

Also add a fallback: if no claim but the request has an `X-Forwarded-TenantId` header (and the request is from a trusted source — for V1, trust any inbound; production should restrict by IP allowlist or mTLS), read from header.

```csharp
public Guid? TenantId
{
    get
    {
        var claim = httpContext.User.FindFirstValue("tenant_id");
        if (Guid.TryParse(claim, out var fromClaim)) return fromClaim;

        var header = httpContext.Request.Headers["X-Forwarded-TenantId"].FirstOrDefault();
        return Guid.TryParse(header, out var fromHeader) ? fromHeader : null;
    }
}
```

Tests in `tests/Application.UnitTests/`:
- Add a `UserContextTests.cs` covering: claim present, header present, neither present, both present (claim wins).

Commit: `feat(api): IUserContext.TenantId resolved from claim or X-Forwarded-TenantId header`

---

## Phase 1 — Replace JWT bearer with introspection

### Task 1.1: `IntrospectionAuthenticationExtensions`

**File:** `src/Infra/Authentication/IntrospectionAuthenticationExtensions.cs`

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Validation.AspNetCore;

namespace Infra.Authentication;

public static class IntrospectionAuthenticationExtensions
{
    public static IServiceCollection AddIntrospectionAuthentication(
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
                o.AddAudiences("api:web");
                o.UseIntrospection()
                    .SetClientId(clientId)
                    .SetClientSecret(clientSecret);
                o.UseSystemNetHttp()
                    .ConfigureHttpClient(c => c.Timeout = TimeSpan.FromSeconds(5));
                o.UseAspNetCore();
            });

        services.AddAuthentication(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);

        return services;
    }
}
```

Note: this mirrors `Gateway.DependencyInjection.AddGatewayAuthentication` but is in `Infra` so multiple entrypoints can reuse it.

Commit: `feat(infra): IntrospectionAuthenticationExtensions for opaque-token validation`

### Task 1.2: Wire into Web.API and remove JWT bearer

**Files:**
- Modify: `src/Infra/DependencyInjection.cs` — remove the `AddJwtBearer(...)` call and the `JWT_SECRET` validation block. Keep everything else (DbContext, password hasher, etc.).
- Modify: `src/EntryPoints/Web.API/Program.cs` — call `services.AddIntrospectionAuthentication(builder.Configuration)`.
- Modify: `src/EntryPoints/Web.API/appsettings.json` — remove `Jwt` section, add `Auth` section with empty values.
- Modify: `src/EntryPoints/Web.API/appsettings.Development.json` — populate dev `Auth` values:
  ```json
  "Auth": {
    "Authority": "http://localhost:5100",
    "IntrospectionEndpoint": "http://localhost:5100/connect/introspect",
    "IntrospectionClientId": "web-api",
    "IntrospectionClientSecret": "dev-only-web-api-secret-change-me"
  }
  ```
- Modify: `compose.yaml` — `web.api` service env vars: remove `JWT_SECRET`, add `Auth__*` keys (use `auth.api:8080` for inter-container).
- Modify: `compose.yaml` — `web.api` service: remove host port mapping (`5010:8080` → no host port, only internal). External clients reach `web.api` only through the gateway. Optional: keep `5010` for direct dev access during transition; document.

Commit: `feat(api): replace JWT bearer with OpenIddict introspection in Web.API`

### Task 1.3: Redis-backed introspection cache (port from Gateway)

**Files:**
- Create: `src/Infra/Authentication/IntrospectionCacheOptions.cs`
- Create: `src/Infra/Authentication/IntrospectionCachingHandler.cs`
- Modify: `src/Infra/Authentication/IntrospectionAuthenticationExtensions.cs` — wire the handler

This is **almost identical** to `src/EntryPoints/Gateway/Authentication/IntrospectionCachingHandler.cs` (Plan 2 Task 3.2). Copy the implementation verbatim into `src/Infra/Authentication/`. Adjust namespace to `Infra.Authentication`. Now both Gateway and Web.API share the same caching pattern.

Wire the same way Plan 2 did (Task 3.3): via `PostConfigure<HttpClientFactoryOptions>("OpenIddict.Validation.SystemNetHttp", ...)`.

Web.API will need:
- `services.AddStackExchangeRedisCache(o => o.Configuration = redisConn);` (configuration[`Redis:ConnectionString`]).
- `services.Configure<IntrospectionCacheOptions>(configuration.GetSection("IntrospectionCache"));`
- The handler registration.

Add `Redis:ConnectionString` to Web.API appsettings + compose env.

Tests in `tests/Web.API.IntegrationTests/Authentication/IntrospectionCachingHandlerTests.cs` — but this is the same handler tested in `tests/Gateway.IntegrationTests/`; duplicating the tests is wasteful. **Option:** move `IntrospectionCachingHandler` from `Gateway` to `Infra` as the canonical impl, delete the Gateway copy, have Gateway reference `Infra.Authentication.IntrospectionCachingHandler`. The 3 unit tests already in `tests/Gateway.IntegrationTests/Authentication/IntrospectionCachingHandlerTests.cs` move to `tests/Web.API.IntegrationTests/Authentication/` (or stay in Gateway tests; the type is now in Infra so Gateway tests still see it via reference). Pick: move the type to `Infra.Authentication`, keep the tests where they are (they reference the type; namespace just changes).

This is a refactor that touches Gateway too. Either:
- Move `IntrospectionCachingHandler` to Infra, update Gateway and Web.API to consume from Infra.
- OR copy-paste the type and accept the duplication.

Pick **move to Infra** — DRY wins; the type is generic enough to live in Infra. Bonus: a `Auth.Infra` future-self may want it too.

Commit: `feat(infra): move IntrospectionCachingHandler to Infra (shared by Gateway + Web.API)`

---

## Phase 2 — Permission policy provider in Web.API

### Task 2.1: Mirror Auth.API's policy provider

**Files:**
- Create: `src/EntryPoints/Web.API/Authorization/PermissionRequirement.cs`
- Create: `src/EntryPoints/Web.API/Authorization/PermissionAuthorizationHandler.cs`
- Create: `src/EntryPoints/Web.API/Authorization/PermissionPolicyProvider.cs`
- Modify: `src/EntryPoints/Web.API/DependencyInjection.cs` — register the policy provider.

This is a **direct copy** of the three classes added to Auth.API in Plan 3 Task 1.1. Adjust namespaces. Same `permission:` policy prefix, same `permission` claim lookup, same `IAuthorizationHandler<PermissionRequirement>`.

To avoid duplication, an alternative is to put these in `Infra.Authorization` and share. **Pick: put in `Infra.Authorization`** (DRY). Web.API and Auth.API both reference Infra. Move Auth.API's copy to Infra at the same time.

Refactor:
- Move `Auth.API/Authorization/Permission*.cs` → `Infra/Authorization/Permission*.cs`. Update Auth.API DI to reference Infra namespaces.
- Web.API's DI registers `IAuthorizationPolicyProvider` from Infra.

Commit: `refactor(infra): centralize PermissionPolicyProvider in Infra (shared by Auth.API + Web.API)`

### Task 2.2: Adopt permission policies on existing Web.API endpoints

**Files:**
- Modify: each `src/EntryPoints/Web.API/Endpoints/SampleEntity/*.cs` — replace `.RequireAuthorization()` with `.RequireAuthorization("permission:sample.read")` or `.RequireAuthorization("permission:sample.write")`.
- Modify: `src/Auth.Domain/Permissions/PermissionCodes.cs` — add `SampleRead = "sample.read"`, `SampleWrite = "sample.write"` to the `All` collection so the seed picks them up.

Update Plan 1's `PermissionSeedHostedService` test expectation (was 9 codes; will be 11 after this change) — fix the affected test in `tests/Auth.Application.UnitTests/Infrastructure/PermissionSeedHostedServiceTests.cs`.

Commit: `feat(api): adopt permission policies on Sample endpoints`

---

## Phase 3 — Tenant context propagation

### Task 3.1: `TenantContextMiddleware`

**Files:**
- Create: `src/EntryPoints/Web.API/Middleware/TenantContextMiddleware.cs`
- Modify: `src/EntryPoints/Web.API/Program.cs` — register middleware after `UseAuthentication` and before `UseAuthorization`.

The middleware mostly logs / instruments; the actual tenant resolution is in `IUserContext`. The middleware's purpose:
- If `X-Forwarded-TenantId` header is present AND `tenant_id` claim is missing, log a debug message saying we're falling back to header.
- If both present and DIFFER, log a warning (potential header-spoofing attempt — production should reject).
- Add an OTel `Activity.Current?.SetTag("tenant.id", tenantId.ToString())` so traces are tenant-tagged.

Skeleton:

```csharp
namespace Web.API.Middleware;

internal sealed class TenantContextMiddleware(RequestDelegate next, ILogger<TenantContextMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var claim = context.User.FindFirstValue("tenant_id");
        var header = context.Request.Headers["X-Forwarded-TenantId"].FirstOrDefault();

        if (!string.IsNullOrEmpty(claim) && !string.IsNullOrEmpty(header) && claim != header)
        {
            logger.LogWarning(
                "Tenant id mismatch: claim={Claim} header={Header}. Trusting claim.",
                claim, header);
        }

        var tenantId = string.IsNullOrEmpty(claim) ? header : claim;
        if (!string.IsNullOrEmpty(tenantId))
        {
            System.Diagnostics.Activity.Current?.SetTag("tenant.id", tenantId);
        }

        await next(context);
    }
}
```

Register in `Program.cs`:

```csharp
app.UseAuthentication();
app.UseMiddleware<TenantContextMiddleware>();
app.UseAuthorization();
```

Commit: `feat(api): TenantContextMiddleware for tenant id propagation and tracing`

### Task 3.2: Add `TenantId` to `SampleEntity`

**Files:**
- Modify: `src/Domain/SampleEntities/SampleEntity.cs` — add `public Guid TenantId { get; private set; }`. Adjust constructor / static factory.
- Modify: `src/Infra/Config/SampleEntityConfiguration.cs` — add column + index `(TenantId, Id)`.
- Modify: `src/Application/SampleEntities/Create/CreateSampleEntityCommandHandler.cs` — accept `IUserContext`, set `TenantId = userContext.TenantId ?? throw new InvalidOperationException(...)`.
- Modify: `src/Application/SampleEntities/GetById/GetSampleEntityByIdQueryHandler.cs` — filter by `e.TenantId == userContext.TenantId`.
- Modify: existing tests in `tests/Application.UnitTests/SampleEntities/` — set up `IUserContext.TenantId` mock.
- Generate EF migration: `dotnet ef migrations add SampleEntityTenantId --project src/Infra --startup-project src/EntryPoints/Web.API --output-dir Database/Migrations`.

Run tests: `dotnet test BaseProjectScaffold.sln -c Release`.

Commit: `feat(api): tenant-scope SampleEntity (column + handlers + migration)`

---

## Phase 4 — Test factory rewrite

### Task 4.1: Replace JWT minter with introspection stub

**File:** `tests/Web.API.IntegrationTests/Infrastructure/CustomWebApplicationFactory.cs`

Currently the factory mints JWTs signed with the `JWT_SECRET` and inserts them via `SetBearerToken`. This needs to be rewritten to:
1. Spawn a WireMock server stubbing `/connect/introspect`.
2. Override `Auth:Authority`, `Auth:IntrospectionEndpoint`, `Auth:IntrospectionClientId/Secret` to point at WireMock.
3. Provide `IssueTestToken(string subjectId, Guid tenantId, params string[] permissions)` that returns a token string AND configures WireMock to respond `active=true` for that token with the right claims.
4. Optionally spawn Redis (Testcontainers) so the introspection cache works.

Mirror the Gateway test factory pattern (`GatewayWebApplicationFactory`) closely — the wiring is essentially identical.

Update `tests/Web.API.IntegrationTests/Endpoints/SampleEntityEndpointTests.cs` — replace `_factory.CreateBearerToken()` calls with `_factory.IssueTestToken(...)` and pass the right permissions.

Commit: `test(api): rewrite CustomWebApplicationFactory for introspection-based auth`

### Task 4.2: Authentication tests

**Files:**
- Create: `tests/Web.API.IntegrationTests/Authentication/IntrospectionAuthTests.cs`

Tests:
- `Endpoint_NoToken_Returns401`
- `Endpoint_InactiveToken_Returns401`
- `Endpoint_ActiveTokenWithoutPermission_Returns403`
- `Endpoint_ActiveTokenWithPermission_Returns200`
- `TenantClaim_ExposedViaIUserContext_OnHandler`

Commit: `test(api): introspection auth integration tests`

### Task 4.3: Permission policy tests

**Files:**
- Create: `tests/Web.API.IntegrationTests/Authentication/PermissionPolicyTests.cs`

Tests:
- `PolicyPrefix_GeneratesDynamicPolicy_PerPermission`
- `Policy_RejectsPrincipalWithoutClaim`
- `Policy_AcceptsPrincipalWithMatchingClaim`

Commit: `test(api): permission policy provider tests`

---

## Phase 5 — Compose, docs, smoke

### Task 5.1: Update compose.yaml

**File:** `compose.yaml`

`web.api` service:
- Remove `JWT_SECRET` env var.
- Add `Auth__*` env vars pointing to `auth.api:8080`.
- Add `Redis__ConnectionString=redis:6379`.
- Remove host port mapping `5010:8080` (Web.API now reachable only through Gateway). **OR** keep mapping for dev convenience and document that production removes it.
- `depends_on: [postgres, rabbitmq, redis, auth.api]`.

Gateway: ensure the `web-api` route in `appsettings.Development.json` and `compose.yaml` env vars points at `web.api:8080` (already there from Plan 2).

Smoke:
```bash
docker compose up -d --build redis auth-postgres auth.api gateway web.api postgres rabbitmq

# Acquire token via Gateway's auth-connect route
TOKEN=$(curl -sf -X POST http://localhost:5200/api/auth/connect/token \
  -d "grant_type=client_credentials" -d "client_id=test-m2m" -d "client_secret=test-secret" \
  -d "scope=api:web" | jq -r .access_token)
# (test-m2m client must be created via the BFF/admin API — for V1 smoke, create manually via the bff page)

# Hit a protected Web.API endpoint via Gateway
curl -i http://localhost:5200/api/v1/sample-entities -H "Authorization: Bearer $TOKEN"

docker compose down
```

The smoke test depends on the BFF being able to create an M2M client (Plan 3 Task 1.6). If that's not yet implemented at execution time, the smoke proves auth wiring works for the seeded `web-api` resource server (which only has Introspection permission, not Token). Document this dependency.

Commit: `chore(api): web.api compose env vars updated for introspection auth`

### Task 5.2: Documentation

**Files:**
- Modify: `CLAUDE.md` — §9 (env vars), §13 (introspection cache TTL, X-Forwarded-TenantId header trust, removing JWT_SECRET).
- Modify: `README.md` — Web.API section updated to reference introspection-based auth.

Commit: `docs: document Web.API integration with Auth.API in CLAUDE.md and README`

---

## Final verification

- [ ] **Step 1: Build + test**

```bash
dotnet build BaseProjectScaffold.sln -c Release
dotnet test BaseProjectScaffold.sln -c Release
```

Expected: 0 errors, 0 warnings. Test count delta: +5-10 from Phase 0 (UserContext) + Phase 4 (auth/policy integration tests). Existing 19 Web.API.IntegrationTests should all migrate cleanly to introspection-based stubs.

- [ ] **Step 2: End-to-end smoke** (described above).

- [ ] **Step 3: Tag**

```bash
git tag -a web-api-integration-v1 -m "Web.API integration complete (Plan 5 of 5) — entire SSO surface live"
```

---

## Self-review

1. **Spec coverage** — Plan 5 covers spec §3 (Web.API behind gateway), §7.4 (M2M flow now end-to-end usable), §8 (downstream policy provider), §10 (env vars). Out of scope: cross-cutting tenant guard in `ApplicationDbContext` (recommended follow-up).

2. **Placeholder scan** — Phase 5 smoke depends on a M2M client created by the BFF. If running standalone, the smoke is partial (depends on Plan 3 admin pages). Documented inline.

3. **Type consistency** — `IUserContext.TenantId` extension respected across `UserContext`, `TenantContextMiddleware`, and the SampleEntity handlers. `IntrospectionCachingHandler` consolidated to `Infra.Authentication` namespace.

4. **Spec items with no task** — `IUserContext` extension also implies tenant-scoping all existing handlers; Phase 3.2 demonstrates the pattern but doesn't enforce it across the existing scaffold (that's the forking team's job per CLAUDE.md §14).

---

## Cross-plan summary

After Plan 5 lands, the full SSO topology described in `docs/superpowers/specs/2026-05-06-sso-auth-design.md` is operational:

```
Browser → Web.Blazor (BFF, cookie) → Gateway (introspection) → Auth.API
                                                              → Web.API
                                                              → (NetSuite via SAML POST)

External M2M client ─Bearer (opaque)──────→ Gateway → Auth.API/Web.API/...
```

- ✅ Single ingress: BFF + Gateway
- ✅ Federated identity: Entra ID upstream
- ✅ Multi-tenant from V1
- ✅ Reference tokens with introspection + Redis cache
- ✅ Permission-based authorization across both Auth.API and Web.API
- ✅ NetSuite outbound SSO
- ✅ Audit log
- ✅ Architecture rules enforced via NetArchTest
- ✅ Integration tests with real Postgres + Redis + WireMock-stubbed Entra

# AGENT_GUIDE.md — How this system works and how to extend it

> **Audience:** AI coding agents (Claude Code, Cursor, Copilot, etc.) and developers ramping up on the codebase.
> **Companion to:** [`CLAUDE.md`](../CLAUDE.md) (the canonical instructions). This guide focuses on how the **assembled system** behaves — the SSO topology, the trust boundaries, the patterns that recur across layers, and the bugs we already paid for.
> **Source of truth ordering:** `CLAUDE.md` rules > this guide > inline comments. If they disagree, fix the disagreement; don't pick a side silently.

---

## 1. Read this first

This codebase is a **production-grade SSO scaffold**. Five plans built it (`docs/superpowers/plans/`), each one stacked on the previous:

| Plan | What it built | Key entrypoints |
|---|---|---|
| 1 | `Auth.API` — OpenIddict OIDC server with reference tokens, multi-tenant, Entra ID federation, JIT user provisioning, M2M client_credentials | `Auth.API`, `Auth.Domain`, `Auth.Application`, `Auth.Infra` |
| 2 | `Gateway` — YARP reverse proxy, single ingress, introspection-based token validation with Redis cache | `Gateway` |
| 3 | `Web.Blazor` BFF — cookie-session OIDC client, Redis-backed token store, MudBlazor admin pages, Auth.API admin HTTP surface | `Web.Blazor`, `Auth.API/Endpoints/Admin/*` |
| 4 | NetSuite SAML — `ITfoxtec.Identity.Saml2` outbound IdP-initiated SSO | `Auth.API/Endpoints/Saml/*`, `Auth.Infra/NetSuite/*` |
| 5 | `Web.API` integration — replaced JWT bearer with introspection, dynamic permission policies, `X-Forwarded-TenantId` propagation, tenant-scoped Sample feature | `Web.API`, `Infra/Authentication/*`, `Infra/Authorization/*` |

If you're touching code, read [`CLAUDE.md`](../CLAUDE.md) for the rigid rules first (architecture direction, sealed handlers, validation pipeline, security hard rules). This guide assumes you already know those.

---

## 2. Run it locally in 30 seconds

```bash
# One-time: dev SAML cert
bash scripts/generate-dev-saml-cert.sh

# Bring up the full stack
docker compose up -d

# Healthchecks
curl -sf http://localhost:5100/health/live              # Auth.API
curl -sf http://localhost:5200/health/ready             # Gateway
curl -sf http://localhost:5002/                         # Blazor BFF (200 = login page)
curl -sf http://localhost:5010/health                   # Web.API direct (still bound for dev convenience)

# Discovery doc through gateway
curl -sf http://localhost:5200/api/auth/.well-known/openid-configuration | jq .issuer
```

Tear down: `docker compose down`. Tear down + wipe data: `docker compose down -v && rm -rf .containers/`.

`bash scripts/generate-dev-saml-cert.sh` writes a self-signed PFX into `src/EntryPoints/Auth.API/dev-certs/` (gitignored). Required only for NetSuite SAML; without it `Auth.API` boots fine but `/saml/netsuite/initiate` throws.

---

## 3. Topology

```
                                     Public network
            ┌─────────────────────────────────────────────────────────┐
            │                                                          │
            │   Browser ────cookie────▶ Web.Blazor (BFF)               │
            │   :5002                                                  │
            │                            │                             │
            │                            │ Bearer (opaque)             │
            │                            ▼                             │
            │   External M2M ─Bearer──▶  Gateway (YARP) :5200          │
            │                            │     │                       │
            └────────────────────────────┼─────┼───────────────────────┘
                                         │     │
                                         │     │  internal network
            ┌────────────────────────────┼─────┼───────────────────────┐
            │                            ▼     ▼                       │
            │     ┌──────────────────────────────────────────┐         │
            │     │ Auth.API :5100      Web.API :5010        │         │
            │     │ (OpenIddict)        (introspection auth) │         │
            │     │     │                       │            │         │
            │     │     ├── /connect/*         │            │         │
            │     │     ├── /admin/*           │            │         │
            │     │     ├── /saml/netsuite/*   │            │         │
            │     │     └── /connect/introspect◀───────────┤         │
            │     └──┬───────────────────┬──────────────────┘         │
            │        │                   │                             │
            │        ▼                   ▼                             │
            │  auth-postgres:5433   redis:6379  (DataProtection,      │
            │  app-postgres:5432                 introspection cache,  │
            │                                    BFF token store)     │
            └──────────────────────────────────────────────────────────┘

            Auth.API ──── SAML POST ────▶ NetSuite ACS
            (outbound IdP-initiated)
```

**Single rule of thumb:** the only public surfaces are `Web.Blazor` (port 5002) and `Gateway` (port 5200). Everything else SHOULD be internal-only in production. Web.API still exposes `5010` in compose for dev convenience — strip that mapping in production.

---

## 4. Trust boundaries

| Direction | What carries trust | Verifier |
|---|---|---|
| Browser → BFF | session cookie (`__Host-Bff-Session`, HttpOnly, Secure, SameSite=Lax) | ASP.NET Core cookie auth handler |
| BFF → Gateway | `Authorization: Bearer <opaque-token>` from BFF's Redis token store | YARP forwards header; downstream introspects |
| Gateway → Auth.API/Web.API | same Bearer header, plus `X-Forwarded-User` and `X-Forwarded-TenantId` (from `Gateway/Authentication/ForwardedIdentityTransform.cs`) | Downstream re-introspects (defense in depth) |
| Web.API → DB | tenant_id resolved from `IUserContext.TenantId` (claim → header fallback) | Handler-level `WHERE tenant_id = ?` |
| Auth.API → NetSuite | signed SAML 2.0 assertion in HTML auto-submit form | NetSuite verifies signature against uploaded cert |
| M2M (external) → Gateway | `client_credentials` grant against Auth.API → opaque token → Gateway introspects | Same as above |

**Rule:** never trust `X-Forwarded-*` headers without re-validating the bearer token. The Gateway sets them; downstream introspects the token AND reads the headers as a convenience. If a request bypasses the Gateway (you forgot to remove `5010:8080` mapping in prod) and a malicious caller sets `X-Forwarded-User`, that's still a bug — fix the network, don't add header validation.

---

## 5. Token model

| Token | Format | Lifetime | Where issued | Where validated |
|---|---|---|---|---|
| `id_token` | Signed JWT (RSA-SHA256) | 15 min | Auth.API `/connect/token` | BFF (claims projected into cookie principal) |
| `access_token` | **Opaque reference** | 15 min (config) | Auth.API `/connect/token` | Auth.API `/connect/introspect` (called by Gateway, Web.API) |
| `refresh_token` | Opaque reference | 14 days (config) | Auth.API `/connect/token` | Auth.API |
| Entra ID tokens | Signed JWTs (Microsoft) | per Entra config | Microsoft | Auth.API at `/signin-entra` callback |
| SAML assertion | Signed XML, base64 in HTML form | seconds (one-shot) | Auth.API `/saml/netsuite/initiate` | NetSuite ACS |

**Why opaque + introspection** (not self-contained JWTs): immediate revocation. Revoke a token → next introspection returns `active=false` (within cache TTL, default 30s). With self-contained JWTs there's no way to revoke before expiry.

**Cache the introspection response, not the validated principal.** The `IntrospectionCachingHandler` (in `src/Infra/Authentication/`) caches the raw JSON response from `/connect/introspect` keyed by `SHA256(token)`. The OpenIddict validation handler unpacks the cached JSON into a fresh principal each time. This means:

- TTL of 30s = max revocation latency.
- Redis cache is shared across Gateway and Web.API — same lookup, same answer.
- `IDistributedCache` is bound to Redis; if Redis is down, the cache shorts out and OpenIddict makes the HTTP call directly (degraded but functional).

---

## 6. The five recurring patterns

Every change you make in this codebase will hit at least one of these. Internalize them.

### 6.1 The Result / Error pattern

Handlers return `Result<T>` or `Result`. **Never** throw for control flow. See `SharedKernel.Result`, `SharedKernel.Error`. Endpoints map via `result.Match(onSuccess, CustomResults.Problem)`.

Error code shape: `<Aggregate>.<Rule>` (`User.NotFound`, `Tenant.Inactive`, `M2MClient.InvalidSecret`). Codes flow into HTTP problem details and into audit log details.

### 6.2 The Validation Pipeline

`Auth.Application.Abstractions.Behaviors.ValidationDecorator` and `Application.Abstractions.Behaviors.ValidationDecorator` (parallel pattern) wrap `ICommandHandler<,>` and `ICommandHandler<>` via Scrutor's `TryDecorate`. **Endpoints inject the interface, never the concrete handler** — the decorator only intercepts the interface.

Validators are FluentValidation, `internal sealed class`, registered via `AddValidatorsFromAssembly(..., includeInternalTypes: true)`.

Queries are NOT decorated — validate inputs manually if needed.

### 6.3 Sealed everywhere

`public sealed class` for handlers, entities, services. `internal sealed class` for validators, endpoints, EF configurations, test handlers. Architecture tests enforce this — see `tests/Auth.API.IntegrationTests/Architecture/ArchitectureTests.cs` and `tests/Web.API.IntegrationTests/Architecture/ArchitectureTests.cs`.

### 6.4 Tenant-scoped queries

Every query against `auth_db` MUST filter by `TenantId` resolved from `ITenantContext`. The `AuthDbContext.SaveChangesAsync` save guard catches missing/wrong `TenantId` on tracked entities — but only for ADDED/MODIFIED entries with a `TenantId` property. Reads are NOT guarded; the handler must filter explicitly.

For the business DB (`app_db` via `ApplicationDbContext`), Plan 5 demonstrated the pattern with `SampleEntity` — handlers inject `IUserContext` and filter on `userContext.TenantId`. Forking teams decide how thoroughly to apply this.

### 6.5 Permission-gated endpoints

Two layers:
1. **Auth.API admin** uses `RequireAuthorization($"{PermissionPolicyProvider.PolicyPrefix}{PermissionCodes.UsersWrite}")`.
2. **Web.API** does the same after Plan 5 — the `PermissionPolicyProvider` was moved to `Infra/Authorization/` so both apps share it.

The provider is a `DefaultAuthorizationPolicyProvider` subclass that translates `permission:<code>` policy names into a dynamic policy with a `PermissionRequirement(code)` that checks `claim.Type == "permission" && claim.Value == code`.

To add a new permission: append a const to `Auth.Domain.Permissions.PermissionCodes.cs` and add it to the `All` collection. The seed hosted service inserts it on next Auth.API startup.

---

## 7. Recipes — adding common features

These are concrete, copy-paste-adapt patterns. Don't reinvent the wheel.

### 7.1 Add a new feature to Web.API (business domain)

**Goal:** a `POST /api/v1/widgets` that requires `widgets.write` permission and is tenant-scoped.

1. **Domain entity** — `src/Domain/Widgets/Widget.cs` inheriting `Entity`, with `TenantId`, sealed, private parameterless ctor, static factory.
2. **Permission codes** — append `WidgetsRead = "widgets.read"`, `WidgetsWrite = "widgets.write"` to `Auth.Domain.Permissions.PermissionCodes.All`.
3. **EF config** — `src/Infra/Config/WidgetConfiguration.cs` mirroring `SampleEntityConfiguration`.
4. **DbContext** — add `DbSet<Widget> Widgets` to `Application.Abstractions.Data.IApplicationDbContext` AND `Infra.Database.ApplicationDbContext`.
5. **Migration** — `dotnet ef migrations add AddWidgets --project src/Infra --startup-project src/EntryPoints/Web.API --output-dir Database/Migrations`.
6. **Application command** — `src/Application/Widgets/Create/CreateWidgetCommand.cs` (record), `CreateWidgetCommandValidator.cs` (FluentValidation, internal sealed), `CreateWidgetCommandHandler.cs` (sealed, injects `IApplicationDbContext` + `IUserContext`, sets `TenantId`).
7. **Endpoint** — `src/EntryPoints/Web.API/Endpoints/Widgets/CreateWidgetEndpoint.cs` (internal sealed `IEndpoint`). Inject `ICommandHandler<CreateWidgetCommand, Guid>` (the **interface**, never the concrete class). Apply `.RequireAuthorization($"{PermissionPolicyProvider.PolicyPrefix}{PermissionCodes.WidgetsWrite}")`.
8. **Tests** — handler unit tests in `tests/Application.UnitTests/Widgets/`, integration test in `tests/Web.API.IntegrationTests/Endpoints/`.

### 7.2 Add a new admin feature to Auth.API (control plane)

**Goal:** a `POST /admin/users/{id}/reset-permissions` admin operation.

1. **Application command** — `src/Auth.Application/Admin/Users/ResetPermissions/ResetUserPermissionsCommand.cs` + Validator + Handler. Inject `IAuthDbContext` and `ITenantContext`. Tenant-scope the query.
2. **HTTP endpoint** — append a route to `src/EntryPoints/Auth.API/Endpoints/Admin/UsersEndpoints.cs`:
   ```csharp
   group.MapPost("/{id:guid}/reset-permissions", async (
       Guid id,
       ICommandHandler<ResetUserPermissionsCommand> handler,
       CancellationToken ct) =>
       {
           var result = await handler.Handle(new ResetUserPermissionsCommand(id), ct);
           return result.Match(() => Results.NoContent(), CustomResults.Problem);
       }).RequireAuthorization($"{PermissionPolicyProvider.PolicyPrefix}{PermissionCodes.UsersWrite}");
   ```
3. **BFF gateway client** — add `ResetUserPermissionsAsync(Guid id, CancellationToken ct)` to `IAdminGatewayClient` and `AdminGatewayClient`.
4. **BFF UI** — add a button to `Components/Pages/Admin/UserDetail.razor` calling the gateway client method, gated by `<PermissionView Permission="users.write">`.
5. **Tests** — handler unit, endpoint integration (use `TestAuthHandler` for permission-bearing principals).

The endpoint pattern is rigid: route group with `/admin/<aggregate>` prefix, per-route `RequireAuthorization` (NEVER on the group — readers should see the policy inline), `CustomResults.Problem` for failures, `Results.NoContent` / `Results.Created` / `Results.Ok` for successes.

### 7.3 Add a route to the Gateway

**Goal:** route `/api/external/foo` to a new `Foo.API` service.

1. Append to `src/EntryPoints/Gateway/appsettings.Development.json`:
   ```json
   "foo-api": {
     "ClusterId": "foo-cluster",
     "Match": { "Path": "/api/external/foo/{**catch-all}" },
     "AuthorizationPolicy": "RequireBearer",
     "Transforms": [{ "PathRemovePrefix": "/api/external/foo" }]
   }
   ```
   And cluster:
   ```json
   "foo-cluster": {
     "Destinations": { "foo": { "Address": "http://localhost:6000/" } }
   }
   ```
2. Append to `compose.yaml` `gateway` service env vars (`ReverseProxy__Routes__foo-api__*` and `ReverseProxy__Clusters__foo-cluster__*`).
3. Add an integration test in `tests/Gateway.IntegrationTests/Routing/` exercising the route via `GatewayWebApplicationFactory`.
4. Update the test factory's "suppression list" if the new route shouldn't fire in tests by default — see `GatewayWebApplicationFactory.ConfigureWebHost` where existing production routes are blanked out for test isolation.

`AuthorizationPolicy: RequireBearer` enforces an authenticated principal at the Gateway. If the route is genuinely public (like `/api/auth/.well-known/*`), omit the policy.

### 7.4 Add a page to the Blazor BFF

**Goal:** a `/admin/widgets` list page.

1. Add DTO records to `src/EntryPoints/Web.Blazor/Gateway/Contracts/AdminContracts.cs` mirroring the Application response shapes.
2. Add methods to `IAdminGatewayClient` and `AdminGatewayClient` (use `AuthorizeAsync(req, ct)` helper to attach the bearer).
3. Create `Components/Pages/Admin/Widgets.razor`:
   ```razor
   @page "/admin/widgets"
   @attribute [Authorize]
   @inject IAdminGatewayClient Gateway
   @inject ISnackbar Snackbar

   <PermissionView Permission="widgets.read">
       <MudDataGrid T="WidgetSummary" ServerData="@LoadWidgets" ... >
           ...
       </MudDataGrid>
   </PermissionView>

   @code {
       private async Task<GridData<WidgetSummary>> LoadWidgets(GridState<WidgetSummary> state)
       {
           try
           {
               var page = await Gateway.ListWidgetsAsync(state.Page + 1, state.PageSize, default);
               return new GridData<WidgetSummary> { Items = page.Items, TotalItems = page.Total };
           }
           catch (Exception ex)
           {
               Snackbar.Add(ex.Message, Severity.Error);
               return new GridData<WidgetSummary> { Items = [], TotalItems = 0 };
           }
       }
   }
   ```
4. Add a `<MudNavLink Href="/admin/widgets">Widgets</MudNavLink>` to `Components/Layout/NavMenu.razor`, wrapped in `<PermissionView Permission="widgets.read">`.
5. Always wrap gateway calls in try/catch + Snackbar.Error — never let an HTTP failure crash the component.

### 7.5 Add a permission code

1. Append to `src/Auth.Domain/Permissions/PermissionCodes.cs` (both the `const` and the `All` list).
2. Update `tests/Auth.Application.UnitTests/Infrastructure/PermissionSeedHostedServiceTests.cs` count expectations.
3. Reference the new code where it's enforced (`RequireAuthorization($"{...}{PermissionCodes.NewCode}")`).
4. Apply it to a Role via the BFF admin UI, or seed it for testing.

The `PermissionSeedHostedService` runs at Auth.API startup and inserts new codes idempotently — no manual SQL needed.

---

## 8. Authorization — end-to-end

Walk through what happens when a user clicks "Disable user" in the BFF:

1. **BFF**: button click → `AdminGatewayClient.DisableUserAsync(id, ct)` → resolves `session_id` claim → reads `SessionTokens` from Redis → sets `Authorization: Bearer <access_token>` → POST `http://gateway:8080/api/auth/admin/users/{id}/disable`.
2. **Gateway**: matches `auth-admin` route. `RequireBearer` policy fires → `OpenIddictValidationAspNetCoreHandler` extracts the bearer → `IntrospectionCachingHandler` checks Redis (`gateway:introspect:<sha256(token)>`) → on miss, POSTs to `Auth.API/connect/introspect` with `client_id=gateway`, `client_secret=...` → caches response 30s → builds `ClaimsPrincipal` from JSON → `RequireAuthenticatedUser()` succeeds.
3. **Gateway**: `ForwardedIdentityTransform` adds `X-Forwarded-User` (sub) and `X-Forwarded-TenantId` headers.
4. **Auth.API**: receives request. Same OpenIddict validation handler, same `IntrospectionCachingHandler` (different Redis key prefix? actually same — both use the same handler in `Infra.Authentication`). Authentication scheme is `OpenIddict.Validation.AspNetCore`.
5. **Auth.API**: route matches `MapPost("/{id:guid}/disable")`. The route's `RequireAuthorization($"permission:users.write")` policy fires.
6. **Auth.API**: `PermissionPolicyProvider.GetPolicyAsync("permission:users.write")` returns a dynamic `AuthorizationPolicy` with `PermissionRequirement("users.write")` + `RequireAuthenticatedUser`.
7. **Auth.API**: `PermissionAuthorizationHandler` checks `principal.HasClaim("permission", "users.write")` → succeeds.
8. **Auth.API**: handler runs. `ITenantContext.TenantId` reads `tenant_id` claim. `IAuthDbContext.Users.FirstOrDefaultAsync(u => u.Id == id && u.TenantId == tenantId)` → load user → `user.Disable()` → `SaveChangesAsync()` → reconciliation logic in `AuthDbContext` materializes any pending join-row deletes (irrelevant here, but it runs). Tenant guard verifies the modified entity's `TenantId` still matches scope.
9. **Auth.API**: returns 204 No Content.
10. **Browser**: BFF page shows snackbar "User disabled".

If the user lacks the `users.write` permission, step 7 returns `Forbid` → 403. If the access token has been revoked, step 2's introspection returns `active=false` (after cache TTL) → 401.

---

## 9. Testing patterns

### 9.1 Test factories (per ingress)

| Factory | Spawns | Purpose |
|---|---|---|
| `tests/Auth.API.IntegrationTests/Infrastructure/AuthWebApplicationFactory.cs` | Postgres testcontainer + Redis testcontainer + WireMock-stubbed Entra | full Auth.API stack |
| `tests/Gateway.IntegrationTests/Infrastructure/GatewayWebApplicationFactory.cs` | Redis testcontainer + WireMock Auth.API + WireMock downstream | Gateway in isolation |
| `tests/Web.Blazor.IntegrationTests/Infrastructure/BffWebApplicationFactory.cs` | Redis testcontainer + WireMock Auth.API + WireMock Gateway | BFF in isolation |
| `tests/Web.API.IntegrationTests/Infrastructure/CustomWebApplicationFactory.cs` | WireMock Auth.API (introspection stub) + EF InMemory + FakeMessagePublisher | Web.API in isolation |

### 9.2 Test authentication scheme

For permission-policy testing in admin endpoints, swapping in a `TestAuthHandler` that synthesizes a principal from `X-Test-Permissions` / `X-Test-TenantId` / `X-Test-UserId` headers is **far cleaner** than going through OpenIddict's token issuance path. See `tests/Auth.API.IntegrationTests/Infrastructure/TestAuthHandler.cs` and `TestPermissionPolicyProvider.cs`.

The factory exposes `CreateAuthorizedClient(tenantId, params permissions)` which pre-loads the headers. Use a `"_"` sentinel for empty permissions so HttpClient doesn't strip the header — that's how you distinguish "authenticated user with zero permissions" (403) from "no auth header" (401).

### 9.3 xUnit collection fixtures

**Lesson learned (Plan 1 + Plan 5):** integration test factories that mutate process-wide state (env vars, OpenIddict discovery cache, etc.) **race** when xUnit runs test classes in parallel. The fix:

```csharp
[CollectionDefinition(Name)]
public sealed class WebApiCollection : ICollectionFixture<CustomWebApplicationFactory>
{
    public const string Name = "WebApi";
}

[Collection(WebApiCollection.Name)]
public sealed class SampleEntityEndpointTests(CustomWebApplicationFactory factory) { ... }
```

A single shared factory backs all tests in the collection. Apply this to every integration test class that uses a shared factory.

### 9.4 Testcontainers caveats

- The first Redis multiplexer connection sometimes fails health checks while Redis is still booting. Pre-warm a singleton multiplexer in `InitializeAsync` and register it as `IConnectionMultiplexer` — the AspNetCore health check then uses the warm one.
- OpenIddict 6.x rejects HTTP requests by default (`ID2083`). For TestServer or HTTP-only dev, set `OpenIddict:DisableTransportSecurityRequirement=true` (config) — this calls `aspnet.DisableTransportSecurityRequirement()`. **Production must run behind TLS** and leave this off.
- OIDC discovery validation in OpenIddict requires a valid JWKS document. WireMock stubs must include a real RSA public key in `keys: [...]` even when tokens are opaque (the JWKS is consumed for `id_token` signature verification).

---

## 10. Multi-tenancy

| Layer | How it works |
|---|---|
| Domain | Multi-tenant entities have a `Guid TenantId` field (`User`, `Group`, `Role`, `M2MClient`, `AuthAuditEvent`). `Permission` is global (no `TenantId`). |
| EF | `AuthDbContext.SaveChangesAsync` enforces the tenant boundary on Added/Modified entries by reflecting on a `TenantId` property. |
| Application | Every query filters by `tenantContext.TenantId`. Reads are NOT guarded by EF — handlers must do it. |
| Identity | Tenant resolved at login time from Entra `tid` claim → looked up in `Tenants` table → tenant.Id put on the local user's principal as `tenant_id` claim. |
| HTTP | Gateway forwards `X-Forwarded-TenantId` from the validated principal. Web.API's `IUserContext` reads claim first, falls back to header. |

**To add a new multi-tenant entity:**

1. Add `Guid TenantId` to the entity (private set).
2. Make every factory require `tenantId` as the first argument.
3. EF config: add `(TenantId, Id)` index. Optionally `(TenantId, NaturalKey)` unique index.
4. Add a `DbSet<T>` to `IAuthDbContext` (Auth bounded context) or `IApplicationDbContext` (business bounded context).
5. Every handler queries via `db.Things.Where(t => t.TenantId == tenantContext.TenantId)`.

**Cross-tenant operations** (rare, support tooling) bypass `ITenantContext` — they use `AuthDbContext` directly with no tenant filter. Document the use case clearly and audit-log the operation.

---

## 11. Cross-cutting concerns

### 11.1 Logging (Serilog)

- `ReadFrom.Configuration(ctx.Configuration)` in every Program.cs. Sinks: Console + Seq (port 5341).
- Use **structured** logging: `logger.LogInformation("User {UserId} did {Action}", userId, action)`.
- Request context middleware in `src/EntryPoints/Web.API/Middleware/RequestContextLoggingMiddleware.cs` enriches every log entry with correlation id + user id. Mirror this pattern when adding ingress endpoints.
- **Never log PII or secrets.** Audit log is the right place for user identity context (it's a separate, retention-controlled stream).

### 11.2 OpenTelemetry

- `Infra.Observability.OpenTelemetryExtensions.AddOpenTelemetryObservability(serviceName, includeAspNetCore, additionalActivitySources)` is the single composition entry point. Every entrypoint calls it.
- ActivitySources: `"Auth.API"`, `"Gateway"`, custom ones can be added per service.
- Tag spans with `tenant.id`, `user.id`, `client.id` (lowercase, dot-separated, OTel naming convention).
- OTLP exporter activates when `OpenTelemetry:OtlpEndpoint` (or `OTEL_EXPORTER_OTLP_ENDPOINT` env var) is set. Without it, providers register but emit nowhere — fine for dev.

### 11.3 Audit log

- `AuthAuditEvent` entity in `Auth.Domain.Audit`. Persisted to `auth.auth_audit_events`.
- **No soft-delete filter** on this table — audit retention is mandatory.
- Tenant save guard **skips** `AuthAuditEvent` because `Auth.API` writes audit rows during `/connect/authorize` BEFORE the principal carries `tenant_id` (the tenant is already resolved separately and stamped onto the row).
- Detail field is `jsonb` — always serialize via `JsonSerializer.Serialize(new { ... })`, never via string interpolation (escaping bugs).
- Truncate `Ip` (45 chars) and `UserAgent` (500 chars) before write to avoid Postgres `22001` errors on long UAs.

### 11.4 Health checks

- `/health/live` — process is up. Always returns 200. No dependency checks.
- `/health/ready` — dependencies reachable. Filter by `Tags.Contains("ready")`. Each entrypoint registers its own dependencies (Auth.API: `auth_db` + Redis; Gateway: Auth.API + Redis; Web.API: `app_db`).

---

## 12. The 12 most useful gotchas (battle-tested)

These are not theoretical — every one bit us during Plans 1-5. Internalize them.

| # | Symptom | Root cause | Fix |
|---|---|---|---|
| 1 | OpenIddict introspection returns `active=false` for fresh tokens | Mixed `Microsoft.IdentityModel.*` versions across transitive deps (e.g., OpenIddict 6.x wants 8.12, ITfoxtec 4.x wants 8.15) | Pin the entire IdentityModel surface in `Directory.Packages.props` with `CentralPackageTransitivePinningEnabled=true`. |
| 2 | Auth.API rejects all requests with `ID2083 "This server only accepts HTTPS requests"` | OpenIddict requires HTTPS by default | Set `OpenIddict:DisableTransportSecurityRequirement=true` in dev config. **Never** in production. |
| 3 | Discovery doc's `issuer` doesn't match endpoints | `OpenIddict:Issuer` not configured; OpenIddict infers from request URL | Always set `OpenIddict:Issuer` to the public URL (Gateway's base URL once Plan 5+ is wired; Auth.API's URL standalone). |
| 4 | Endpoint silently bypasses validation pipeline | Endpoint injects the concrete handler class, not the interface | Always inject `ICommandHandler<,>`. The decorator only intercepts the interface. |
| 5 | Integration test passes alone, fails when run with siblings | xUnit parallelism + process-wide env vars in the factory race | Use `[CollectionDefinition]` + `ICollectionFixture<>` + `[Collection]` to share a single factory per test process. |
| 6 | Reads return rows from another tenant | Handler forgot to filter by `tenantContext.TenantId` | EF query filters help (Auth bounded context), but handlers must add tenant filter explicitly. |
| 7 | M2M clients can't get a token from `/connect/token` | Client was seeded with only `Endpoints.Introspection` permission | Add `Endpoints.Token + GrantTypes.ClientCredentials + scopes` permissions; resource servers (`web-api`, `gateway`) keep introspection-only. |
| 8 | Modified collection on a domain aggregate doesn't persist | EF doesn't see the change because `_roleIds.Add(x)` doesn't flip the entity to `Modified` | `AuthDbContext.ReconcileMembershipsAsync` iterates **Unchanged** entries too and diffs in-memory collection vs DB join rows. Don't bypass it. |
| 9 | Refresh-token grant returns stale permissions | Permissions are claims on the issued token; refresh by default copies them | The `/connect/token` endpoint refreshes the `permission` claim list on `IsRefreshTokenGrantType()` by re-resolving via `IPermissionResolver`. |
| 10 | OIDC discovery times out from BFF | `RequireHttpsMetadata=true` against an HTTP authority | In dev set `RequireHttpsMetadata=false`. Production: terminate TLS at the gateway and have the BFF speak HTTPS to the gateway. |
| 11 | `auth.api` container starts but immediately crashes with "relation auth.permissions does not exist" | Migration not applied to a fresh database | Auto-migrate on startup gated by `app.Environment.IsDevelopment()`. Production deploys via `dotnet ef database update` in the pipeline. |
| 12 | "WebMock" or "WireMock" introspection stub returns active but Gateway still 401s | OpenIddict validates `aud`, `iss` claims against config | Stub MUST include `aud: ["api:web"]` (or whatever you configured in `AddAudiences(...)`) AND `iss` matching `Auth:Authority`. |

---

## 13. When to break which rule

The rules in this guide and `CLAUDE.md` are tight on purpose, but you'll hit cases where bending one is the right move. Here's the calculus:

| Rule | When OK to bend | When NOT |
|---|---|---|
| "Domain has no dependency on EF Core" | Never. Even an attribute. Domain stays pure. | Always. |
| "Application has no dependency on AspNetCore" | Never. Application is testable in isolation. | Always. |
| "Handlers are sealed" | Never. Architecture test enforces. | Always. |
| "Endpoints inject the interface" | Never. Bypasses ValidationDecorator. | Always. |
| "All endpoints `RequireAuthorization`" | Public discovery (`/.well-known/*`), health checks, login redirect (`/login`), signin callback (`/signin-oidc`). Document why. | Anywhere else: explicit `.AllowAnonymous()` in PR. |
| "Tenant scope every query" | Cross-tenant admin tooling (rare). Justify in PR; audit-log the operation. | Default reads. |
| "All entities `IsDeleted` filtered" | `AuthAuditEvent` (compliance retention). | Everywhere else, soft-delete is the default. |
| "Use `Result<T>`, not exceptions" | Truly exceptional invariants (e.g., misconfigured cert at startup → `InvalidOperationException` and let the host crash). | Domain validation, business rules, expected error paths. |
| "Reference tokens via introspection" | Internal `id_token` for the BFF (signed JWT). The `id_token` is NOT validated downstream — it's consumed by the BFF only. | Access tokens, refresh tokens — always reference. |

**The pattern:** if you want to break a rule, the question isn't "can I?" but "can I justify it in writing in the PR description and add a comment in the code?". If yes, do it. If you're vague, don't.

---

## 14. Forking checklist

When you fork this scaffold to start a new product:

1. Rename `BaseProjectScaffold.sln` and update all `<UserSecretsId>` GUIDs (regenerate them; don't reuse).
2. Replace `SampleEntity` with your first business aggregate (in `Domain/`, `Application/`, `Infra/Config/`, `Web.API/Endpoints/`, all tests). Generate a fresh migration.
3. Update `Jwt:Issuer` / `Auth:Authority` in `appsettings.json` to your domain.
4. Update `compose.yaml` service names, DB names, ports.
5. **Generate fresh Entra ID app registration** (or whatever IdP you federate with). Set client_id, client_secret, redirect URIs.
6. **Generate fresh OpenIddict signing certs** (NOT dev cert) and store in your secret manager. Configure `OpenIddict:SigningCertificatePath` env var. Same for encryption cert.
7. **Generate fresh NetSuite SAML signing cert** if using NetSuite SSO. Upload public cert to NetSuite SP setup.
8. **Rotate ALL `OPENIDDICT_*_SECRET` env vars**. The dev defaults (`dev-only-*-secret-change-me`) MUST never reach production — `OpenIddictClientSeedHostedService.ResolveSecret` already throws in non-Development environments.
9. Decide on a real Postgres + Redis HA setup (RDS + ElastiCache, Azure Database + Cache, etc.).
10. Set up a real OTLP collector / observability backend (Datadog, Honeycomb, Grafana, Aspire dashboard).
11. Drop or adapt `Web.Blazor` if you don't want a backoffice. The Auth.API admin endpoints are still useful (you'll just call them from a different UI).
12. Update `CLAUDE.md` and this `AGENT_GUIDE.md` to remove scaffold-specific notes; keep rules that still apply.
13. Tag the fork point. The original scaffold's commit history is your forensic trail when something behaves unexpectedly.

---

## 15. Where to look when stuck

| Task | First file to read |
|---|---|
| New auth-related feature | `docs/superpowers/specs/2026-05-06-sso-auth-design.md` |
| Adding to Auth.API | `src/EntryPoints/Auth.API/Program.cs` (composition root) + `Endpoints/Admin/UsersEndpoints.cs` (template) |
| Adding to Gateway | `src/EntryPoints/Gateway/appsettings.Development.json` (routes) + `Program.cs` |
| Adding to BFF | `src/EntryPoints/Web.Blazor/Program.cs` + `Components/Pages/Admin/Users.razor` (template) |
| Adding a Web.API endpoint | `src/EntryPoints/Web.API/Endpoints/SampleEntity/CreateSampleEntityEndpoint.cs` (template) |
| Token flow not working | Check `IntrospectionCachingHandler` Redis key + Auth.API logs at `/connect/introspect`. WireMock requests visible in `factory.AuthApi.LogEntries`. |
| EF Core migration acting up | `src/Auth.Infra/Database/AuthDbContextDesignTimeFactory.cs` (Auth) or `src/Infra/Database/ApplicationDbContextDesignTimeFactory.cs` (business). Ensure `dotnet ef` is the right version (`.config/dotnet-tools.json` pins it). |
| Test passes alone, fails parallel | Section 9.3 — collection fixtures. |
| New permission code | Section 7.5 — three lines + a test count update. |
| SAML to a new SP (besides NetSuite) | `src/Auth.Infra/NetSuite/NetSuiteSamlSigner.cs` — clone, parameterize. |

---

## 16. What this guide intentionally does NOT cover

- **Build chain** (`Directory.Packages.props`, `Directory.Build.props`, `nuget.config`, `global.json`): see `CLAUDE.md` §4.
- **Style & naming**: see `CLAUDE.md` §12.
- **The Result/Error factory APIs**: see `SharedKernel/Result.cs` and `Error.cs` directly.
- **Specific OpenIddict 6.4.0 API quirks**: read `~/.nuget/packages/openiddict.*` source. The package's own docs at <https://documentation.openiddict.com> are accurate but lag the latest minor version.
- **Specific MudBlazor 8.x component APIs**: <https://mudblazor.com/components>.
- **Production deployment**: this scaffold ships dev-friendly defaults (HTTP, dev certs, in-memory Quartz store). A separate production hardening guide is a follow-up.

When you encounter something not covered here, **add it** in the same PR. This guide is meant to grow with the codebase, not to be a snapshot in time.

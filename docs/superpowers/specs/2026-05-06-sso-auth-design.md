# SSO Auth.API + BFF Blazor + Gateway YARP — Design

**Status:** Approved (design phase)
**Date:** 2026-05-06
**Author:** Diego Modesto (with Claude)
**Scope:** Add a standalone identity service with Microsoft (Entra ID) and NetSuite SSO support to the existing scaffold, plus a Blazor backoffice (BFF) and a YARP reverse proxy gateway. Multi-tenant from day one. Introspection-based token validation.

---

## 1. Goals

- Centralize authentication and authorization for all current and future services in this solution behind a single identity service (`Auth.API`).
- Federate Microsoft Entra ID as the upstream identity provider for human users.
- Issue SAML 2.0 assertions outbound so users SSO into NetSuite without a second login.
- Provide a Blazor backoffice acting as a BFF for human admins (cookie session, token never reaches the browser).
- Route all server-to-server traffic through a YARP reverse-proxy gateway. `Auth.API` and downstream services are not exposed to the internet directly.
- Support multi-tenant identity from V1 (data model, claims, and OIDC clients are tenant-aware) even if production starts single-tenant.
- Allow immediate token revocation via OAuth 2.0 introspection (RFC 7662).
- Honor the existing scaffold's Clean Architecture / Result-Error / Validation Pipeline / NetArchTest conventions.

## 2. Non-goals (V1)

- RabbitMQ identity events (UserCreated, RoleAssigned, …). Hooks may be added later; not in V1.
- Local password authentication / self-service password reset. All users authenticate through Entra ID.
- MFA implemented locally. Entra provides it.
- Mobile or third-party API clients. Gateway is ready for them, but no client exists in V1.
- Per-tenant issuer / per-tenant signing keys. V1 uses a single issuer; tenant identity flows via the `tenant_id` claim.

## 3. Topology

```
┌──────────┐     cookie     ┌─────────────┐    Bearer     ┌────────────┐
│ Browser  │ ──────────────▶│ Web.Blazor  │──────────────▶│   Gateway  │
└──────────┘                │   (BFF)     │               │   (YARP)   │
                            └─────┬───────┘               └─────┬──────┘
                                  │OIDC                         │ Bearer
                                  ▼                             ▼
                            ┌─────────────────────────────────────┐
                            │   Auth.API (OpenIddict + ITfoxtec)  │◀── client_credentials
                            └────────────┬────────────────────────┘     (Worker, CronJobs)
                                         │OIDC (federated upstream)             │
                                         ▼                                      │
                                   ┌──────────┐                                 │
                                   │ Entra ID │                                 │
                                   └──────────┘                                 │
                                                                                ▼
                            ┌─────────────────────────────────────┐      ┌────────────┐
                            │  Auth.API ────SAML POST────▶ NetSuite      │  Web.API   │
                            └─────────────────────────────────────┘      └────────────┘
```

**Public ingress points:** only `Web.Blazor` (cookie-based) and `Gateway` (Bearer-token-based). Auth.API, Web.API, Worker, and CronJobs are not directly reachable from the public network — Gateway is the single hop in.

**Token type:** all access tokens (user and M2M) are **opaque reference tokens**. The Gateway and downstream services validate them via `POST /connect/introspect` against Auth.API. The OIDC `id_token` remains a signed JWT (consumed only by the BFF).

## 4. Stack decisions

| Concern | Choice | Rationale |
|---|---|---|
| OIDC / OAuth 2.0 server | **OpenIddict** | OSS, free, mature, EF Core native, single-binary distribution. |
| SAML 2.0 IdP-initiated SSO | **ITfoxtec.Identity.Saml2** | OSS, NetSuite-compatible, decoupled from OpenIddict. |
| Reverse proxy | **YARP** | First-party Microsoft, native .NET, integrates with auth handlers. |
| Identity DB | **PostgreSQL** (`auth_db`, separate from `app_db`) | Matches scaffold; isolation of identity data. |
| Cache (introspection + sessions) | **Redis** (via `IDistributedCache`) | Required for HA; shared across services. |
| Token format | **Reference (opaque)** for access; **JWT** for ID token | Reference tokens enable immediate revocation via introspection. |
| Federated upstream IdP | **Entra ID (OIDC code+PKCE)** | Single source of truth for human identity. |
| Outbound SAML SP | **NetSuite** | IdP-initiated SAML POST binding. |

## 5. Solution layout

New projects added alongside the existing scaffold (existing `Domain` / `Application` / `Infra` and entrypoints stay untouched and continue to host the "Sample" business domain).

```
src/
├── Auth.Domain/                     # NEW — Tenant, User, Group, Role, Permission, M2MClient,
│                                            EntraGroupLink, NetSuiteMapping
├── Auth.Application/                # NEW — Commands/Queries (JIT user sync, role assignment,
│                                            SAML assertion issuance, M2M client CRUD, …)
├── Auth.Infra/                      # NEW — EF Core (PostgreSQL), OpenIddict, ITfoxtec,
│                                            Entra OIDC client, Redis introspection cache
└── EntryPoints/
    ├── Auth.API/                    # NEW — OpenIddict endpoints, /saml/netsuite, internal /admin
    └── Gateway/                     # NEW — YARP, validates tokens via introspection

tests/
├── Auth.Domain.UnitTests/           # NEW
├── Auth.Application.UnitTests/      # NEW
└── Auth.API.IntegrationTests/       # NEW — OIDC/SAML flow + Architecture tests
```

`Auth.*` is a separate bounded context with its own DbContext and migrations. The architectural rule "Domain has no dependency on Infra/EF" applies the same way (enforced via NetArchTest).

## 6. Data model (`auth_db`)

OpenIddict-managed tables: `OpenIddictApplications`, `OpenIddictAuthorizations`, `OpenIddictTokens`, `OpenIddictScopes`.

Custom tables (all with `TenantId` from V1):

```
Tenants(Id PK, EntraTenantId UNIQUE, DisplayName, IsActive, DefaultRedirectUri, CreatedAt)
Users(Id PK, TenantId FK, EntraOid, Email, NetSuiteEmail, DisplayName,
      IsActive, IsPreProvisioned, CreatedAt, LastLoginAt,
      UNIQUE(TenantId, EntraOid), UNIQUE(TenantId, Email))
Groups(Id PK, TenantId FK, Name, EntraGroupId NULL, Description,
       UNIQUE(TenantId, Name))
Roles(Id PK, TenantId FK, Name, Description, UNIQUE(TenantId, Name))
Permissions(Id PK, Code UNIQUE, Description)             -- global, seeded
UserGroups(UserId FK, GroupId FK, PK(UserId, GroupId))
UserRoles(UserId FK, RoleId FK, PK(UserId, RoleId))      -- direct override
GroupRoles(GroupId FK, RoleId FK, PK(GroupId, RoleId))
RolePermissions(RoleId FK, PermissionId FK, PK(RoleId, PermissionId))
M2MClients(ClientId PK, TenantId FK, ClientSecretHash, DisplayName,
           AllowedScopes jsonb, IsActive)
AuthAuditLog(Id PK, TenantId FK, UserId FK NULL, EventType, Ip,
             UserAgent, Detail jsonb, OccurredAt)
```

**Effective permissions** of a user = `RolePermissions` joined via `UserRoles` ∪ `GroupRoles` (where `UserGroups` matches the user). Materialized into the access token on issuance as repeated `permission` claims.

**Tenant scoping invariant:** every query in `Auth.Application` filters by `TenantId`. The current tenant is resolved from the OIDC `tid` claim (Entra) or from the authenticated `M2MClient.TenantId`. A unit-of-work guard in `Auth.Infra` enforces that no SaveChanges may persist a row whose `TenantId` differs from the current `IUserContext.TenantId`.

## 7. Authentication flows

### 7.1 Browser login (Blazor BFF → Auth.API → Entra)

1. User clicks **Login** → BFF issues OIDC `code+PKCE` request to `Auth.API/connect/authorize`.
2. Auth.API redirects to Entra ID authorize endpoint (federated).
3. User authenticates at Entra; Entra returns `id_token` to Auth.API callback.
4. Auth.API resolves `tid` → `Tenants` row. If tenant missing or inactive → reject.
5. Auth.API runs **JIT provisioning**: find user by `(TenantId, EntraOid)`. If not found and not pre-provisioned, create. If found but `IsActive=false`, reject.
6. Auth.API generates its own authorization code, redirects to BFF callback.
7. BFF exchanges code at `/connect/token` for `(access_token, refresh_token, id_token)`.
8. BFF stores tokens **server-side** in Redis keyed by session id, sets cookie `__Host-Auth` (`HttpOnly`, `Secure`, `SameSite=Lax`).

### 7.2 Browser → Gateway → Service

1. Browser → BFF (cookie). BFF resolves `access_token` from session cache.
2. BFF calls Gateway with `Authorization: Bearer <reference_token>`.
3. Gateway validates the token via `POST /connect/introspect` (cache TTL 30 s in Redis). On `active=true`, claims are projected onto the forwarded request.
4. Gateway forwards to the destination service with the same `Authorization` header. Service revalidates the same way (or trusts gateway via signed `X-Forwarded-Identity` — V1 picks **revalidate** for defense in depth).

### 7.3 Outbound SAML to NetSuite

1. User clicks **Open NetSuite** in Blazor → BFF calls `Auth.API/saml/netsuite/initiate`.
2. Auth.API generates a signed SAML 2.0 assertion via ITfoxtec:
   - `NameID = User.NetSuiteEmail` (format `emailAddress`)
   - `Audience = NETSUITE_SAML_AUDIENCE`
   - `Recipient = NETSUITE_SAML_ACS_URL`
   - Signing key from `NETSUITE_SAML_SIGNING_CERT_PATH`
3. Auth.API responds with an HTML auto-submit form `POST` to NetSuite ACS URL. NetSuite consumes the assertion and starts a session.
4. Failure modes: missing `NetSuiteEmail` returns 409 with `Error.Validation("User.NetSuiteEmailMissing")`.

### 7.4 Service-to-service (M2M)

1. Service is registered as an `M2MClient` (tenant-scoped). Secret hashed with PBKDF2 (reuse `IPasswordHasher`).
2. Service requests a token at `/connect/token` with `grant_type=client_credentials`. Cached in-process for `expires_in - 60s`.
3. Service calls Gateway with the token. Gateway introspects exactly like a user token.

### 7.5 Revocation

- `/connect/revocation` (RFC 7009) marks token as `revoked` in `OpenIddictTokens`.
- Backoffice "Revoke all sessions for user" updates all tokens for `Subject=@userId, TenantId=@tenant`.
- Effective revocation latency = introspection cache TTL (30 s default, configurable).

## 8. Authorization model in downstream services

- `Web.API` (and any future service) registers `JwtBearer` configured for **introspection** (using OpenIddict client validation handler, not stock JWT validator).
- A single attribute-friendly policy `RequirePermission("users.write")` is implemented through a dynamic `IAuthorizationPolicyProvider` that synthesizes a policy per permission code on demand (avoids registering one policy per permission).
- Endpoints continue to call `.RequireAuthorization(...)` with permission codes.
- The `tenant_id` claim is exposed on `IUserContext.TenantId` for downstream filtering.

## 9. Backoffice (Blazor BFF) — V1 scope

Pages, all gated by their respective permissions:

- **Login** — Entra federated.
- **Users** — list, view detail, activate/deactivate, pre-provision, edit `NetSuiteEmail`, assign direct roles.
- **Groups** — CRUD, link `EntraGroupId`, attach roles.
- **Roles** — CRUD, attach permissions.
- **Permissions** — read-only, seeded list.
- **M2M Clients** — create, list, regenerate secret, deactivate. Secret shown once on creation.
- **NetSuite** — "Open NetSuite" button per user (admin) and "Open my NetSuite" (self).
- **Audit log** — paginated, filterable by user / event type / date.

Components inject `ICommandHandler<,>` / `IQueryHandler<,>` interfaces (never concrete) — same rule as Web.API endpoints.

## 10. Configuration (env vars added)

```
AUTH_DB_CONNECTION_STRING
REDIS_CONNECTION_STRING

ENTRA_TENANT_ID
ENTRA_CLIENT_ID
ENTRA_CLIENT_SECRET
ENTRA_AUTHORITY                           # https://login.microsoftonline.com/{tenant}/v2.0

NETSUITE_SAML_ACS_URL
NETSUITE_SAML_AUDIENCE
NETSUITE_ACCOUNT_ID
NETSUITE_SAML_SIGNING_CERT_PATH
NETSUITE_SAML_SIGNING_CERT_PASSWORD

OPENIDDICT_SIGNING_CERT_PATH
OPENIDDICT_ENCRYPTION_CERT_PATH
JWT_ISSUER                                # public URL of Auth.API

GATEWAY_DOWNSTREAM_AUTH_API               # internal URL of Auth.API
GATEWAY_DOWNSTREAM_WEB_API                # internal URL of Web.API
GATEWAY_INTROSPECTION_CACHE_TTL_SECONDS   # default 30
```

`compose.yaml` adds: `auth-postgres`, `redis`, `auth-api`, `gateway`, `blazor-bff`. Existing `Web.API` / `Worker` / `CronJobs` services move behind the gateway (no public port mappings).

## 11. Conventions inherited from the scaffold

- `Result<T>` / `Error` factories — no exceptions for control flow.
- `ValidationDecorator` over `ICommandHandler<,>` (and `<>`) — endpoints inject the **interface**, never the concrete handler.
- `sealed` on every handler and endpoint — enforced by NetArchTest, extended to cover `Auth.*`.
- Serilog structured logging, request context middleware.
- OpenTelemetry: `Auth.API`, `Gateway`, and `Web.Blazor` all call `AddOpenTelemetryObservability(...)` with their own service name.
- File-scoped namespaces, primary constructors, records for DTOs/commands/queries/responses.
- Internal validators / endpoints / EF configurations via `InternalsVisibleTo`.

## 12. Testing

- **`Auth.Domain.UnitTests`** — entity invariants, permission resolution algorithm.
- **`Auth.Application.UnitTests`** — handler tests with `Moq`/EF InMemory; validator tests with `FluentValidation.TestHelper`.
- **`Auth.API.IntegrationTests`** — `WebApplicationFactory`-based; mocks Entra OIDC discovery and userinfo via `WireMock.Net`. Covers happy paths, JIT provisioning, tenant resolution failure, disabled user, missing NetSuite email, revocation propagation.
- **Architecture tests** mirror the existing rules and add:
  - `M2MClient` factories live only in `Auth.Infra`.
  - Auth.Application has no dependency on `OpenIddict.Server.AspNetCore`.
  - All Auth handlers/endpoints are `sealed`.
- **Smoke**: a docker-compose-based end-to-end test boots `auth-postgres`, `redis`, `auth-api`, `gateway`, performs login against a stubbed Entra, exchanges tokens, and calls a Web.API endpoint.

## 13. Operational concerns

- **HA**: Auth.API and Gateway are on the request hot path — minimum 2 replicas in production. Both are stateless (state in PostgreSQL + Redis).
- **Signing key rotation**: OpenIddict supports rolling certificates. JWKS exposes new + previous key during transition. Operational policy: rotate every 90 days.
- **Cache cold start**: first request per token pays 10–30 ms for introspection round-trip. Acceptable.
- **Health checks**: `/health/live` and `/health/ready` on Auth.API and Gateway; gateway readiness depends on Auth.API readiness.
- **Audit**: every authentication, token issuance, revocation, and admin mutation writes to `AuthAuditLog`.
- **Backups**: `auth_db` daily snapshot; OpenIddict tokens are recoverable but tenant/user/role data is the durable asset.

## 14. Risks

| Risk | Mitigation |
|---|---|
| Auth.API becomes a single point of failure | Stateless replicas + health checks + Redis-backed shared state |
| Introspection adds latency to every request | 30s Redis cache; tunable per environment |
| Tenant filtering bug leaks data across tenants | Save-time guard in `AuthDbContext`; integration test that crosses tenants and asserts 404 |
| OpenIddict signing certificate exposed | Stored only in secret manager, never in repo or `appsettings.json` |
| NetSuite signing certificate compromise | Same; rotate via NetSuite SAML config admin UI |
| `AuthAuditLog` growing unbounded | Partitioning by `OccurredAt` month; retention policy 12 months |

## 15. Open questions for implementation phase

- Identity for the BFF client itself in OpenIddict: one `OpenIddictApplication` per tenant or one shared? V1 picks **one shared** (BFF runs on a fixed origin) — revisit if multi-tenancy gets per-tenant subdomains.
- Whether the Gateway should re-emit identity to downstream via `X-Forwarded-Identity` (signed) for performance, or always require downstream introspection. V1: **always introspect**. Optimize if measurements demand it.
- Whether to support `prompt=none` silent renewal in the BFF (allows seamless token refresh without redirect). V1: **yes**, since BFF is the only browser client and the UX matters.

---

**Approvals**
- Design discussion: 2026-05-06 (Diego ↔ Claude)
- Implementation plan: pending (`writing-plans` skill, next step)

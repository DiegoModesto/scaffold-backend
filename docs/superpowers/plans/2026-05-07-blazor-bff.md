# Blazor BFF Backoffice Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Convert the existing scaffold's `Web.Blazor` entrypoint into a Backend-For-Frontend (BFF) that authenticates browser users via Auth.API (OIDC code+PKCE), persists tokens server-side in Redis, and proxies authenticated calls to the Gateway. Add admin pages (Users, Groups, Roles, Permissions, M2M Clients, Audit log) gated by permission claims.

**Architecture:** `Web.Blazor` becomes an OIDC client of `Auth.API` (client_id `bff-blazor`, already seeded). Sign-in flow: user clicks Login → OIDC challenge to Auth.API → Auth.API redirects to Entra → callback → Auth.API issues code → BFF exchanges code at `/connect/token` → BFF stores tokens in Redis keyed by session id → cookie `__Host-Bff-Session` sets the session id (`HttpOnly`, `Secure`, `SameSite=Lax`). For backend calls: Razor components inject a typed `IGatewayClient` that resolves the access token from session storage and calls the Gateway's `/api/v1/...` routes. Token never reaches the browser.

**Tech Stack:** .NET 10, Blazor Server (existing scaffold), `Microsoft.AspNetCore.Authentication.OpenIdConnect 10.0.7`, `Microsoft.AspNetCore.Authentication.Cookies 10.0.7`, `StackExchange.Redis 2.8.16`, `MudBlazor 8.x` (component library — pick latest), xUnit + bUnit + Shouldly + WireMock for tests.

**Out of scope (deferred):**
- Plan 4: NetSuite SAML "Open NetSuite" button.
- Plan 5: Web.API integration (BFF currently only consumes Auth.API admin endpoints; rich Web.API integration comes when Web.API is gated).
- SignalR / push notifications (V2).
- Localization beyond pt-BR / en-US.
- Auth.API admin endpoints (CRUD APIs over Users/Groups/Roles/Permissions/M2MClients) — **this plan adds them as part of Auth.API surface** because the BFF has no other consumer.

---

## Context for the implementer

- Branch: `feature/blazor-bff` already created from `feature/gateway-yarp` HEAD. Your work stacks on Plan 2.
- The existing `Web.Blazor` entrypoint is the .NET 10 default Blazor Server template (read `src/EntryPoints/Web.Blazor/`):
  - `Components/` (Razor components)
  - `Program.cs` (composition root, currently uses JWT bearer)
  - `appsettings.json` (currently has `Jwt:Secret` and DB connection — those are for the Web.API workflow that hasn't been touched yet; you'll set up new Auth/Redis config alongside)
- The existing `bff-blazor` OpenIddict client is seeded by `OpenIddictClientSeedHostedService` with:
  - `client_id=bff-blazor`
  - `client_secret=dev-only-bff-secret-change-me`
  - `RedirectUris={ "http://localhost:5002/signin-oidc" }`
  - `PostLogoutRedirectUris={ "http://localhost:5002/signout-callback-oidc" }`
  - Permissions: AuthorizationCode + RefreshToken grants, openid+profile+email+offline_access scopes, plus `api:web` scope.
- The Gateway routes `/api/auth/connect/*` and `/api/auth/.well-known/*` to Auth.API. So the BFF's OIDC discovery must point at the Gateway: `http://localhost:5200/api/auth/.well-known/openid-configuration`. **For this plan we point the BFF directly at Auth.API's URL (`http://localhost:5100`)** — keeps things simpler for V1; routing through the gateway is a hardening follow-up.
- CLAUDE.md §2: Blazor BFF is allowed to inject `ICommandHandler<,>` / `IQueryHandler<,>` directly (it's an entrypoint, not Application/Domain). For this plan, **the BFF will NOT inject Auth.* handlers**. It calls Auth.API admin endpoints over HTTP. The BFF stays a clean OIDC client.

---

## File structure

```
src/Auth.Application/
├── Admin/                                 # NEW — admin commands/queries
│   ├── Users/
│   │   ├── ListUsers/                     # ListUsersQuery + Handler + Response
│   │   ├── GetUser/                       # GetUserByIdQuery + Handler + Response
│   │   ├── PreProvision/                  # PreProvisionUserCommand + Validator + Handler
│   │   ├── Disable/                       # DisableUserCommand + Handler
│   │   ├── Enable/                        # EnableUserCommand + Handler
│   │   ├── SetNetSuiteEmail/              # SetNetSuiteEmailCommand + Validator + Handler
│   │   └── AssignRole/                    # AssignRoleToUserCommand + Handler
│   ├── Groups/                            # ListGroupsQuery, CreateGroupCommand, ...
│   ├── Roles/                             # ListRolesQuery, CreateRoleCommand, ...
│   ├── Permissions/                       # ListPermissionsQuery (read-only)
│   ├── M2MClients/                        # ListM2MClientsQuery, CreateM2MClientCommand, ...
│   └── Audit/                             # ListAuditEventsQuery (paginated)

src/EntryPoints/Auth.API/
├── Endpoints/
│   ├── Admin/                             # NEW — HTTP surface for the BFF
│   │   ├── UsersEndpoints.cs              # GET /admin/users, GET /admin/users/{id}, POST/PATCH/DELETE
│   │   ├── GroupsEndpoints.cs
│   │   ├── RolesEndpoints.cs
│   │   ├── PermissionsEndpoints.cs        # GET only
│   │   ├── M2MClientsEndpoints.cs
│   │   └── AuditEndpoints.cs              # GET with paging
│   └── Authorization/
│       └── PermissionRequirement.cs       # IAuthorizationRequirement + handler — checks `permission` claim

src/EntryPoints/Web.Blazor/
├── Authentication/
│   ├── BffAuthenticationExtensions.cs     # AddBffAuthentication: cookie + OIDC code+PKCE
│   ├── TokenStore/
│   │   ├── ITokenStore.cs
│   │   └── RedisTokenStore.cs             # stores access_token + refresh_token + id_token by session id
│   └── BffSessionMiddleware.cs            # mints session id cookie, attaches token store key to HttpContext
├── Gateway/
│   ├── IAdminGatewayClient.cs             # typed HttpClient interface for admin endpoints
│   └── AdminGatewayClient.cs              # impl that adds Authorization header from token store
├── Components/
│   ├── App.razor                          # existing, maybe lightly modified
│   ├── Routes.razor                       # existing
│   ├── Layout/
│   │   ├── MainLayout.razor               # add header with user info + logout
│   │   ├── NavMenu.razor                  # add Admin nav items (gated by permission claims)
│   │   └── LoginButton.razor              # NEW
│   ├── Pages/
│   │   ├── Home.razor                     # rewrite as login landing
│   │   ├── Admin/
│   │   │   ├── Users.razor                # list + filters
│   │   │   ├── UserDetail.razor           # detail + actions (disable, set NS email, assign role)
│   │   │   ├── Groups.razor
│   │   │   ├── GroupDetail.razor
│   │   │   ├── Roles.razor
│   │   │   ├── RoleDetail.razor
│   │   │   ├── Permissions.razor          # read-only list
│   │   │   ├── M2MClients.razor
│   │   │   ├── M2MClientDetail.razor      # create + show secret once
│   │   │   └── Audit.razor                # paginated log view
│   │   ├── SignedOut.razor                # post-logout landing
│   │   └── AccessDenied.razor
│   └── Shared/
│       ├── PermissionView.razor           # render children only if user has permission
│       └── ErrorBoundary.razor            # graceful errors
├── Program.cs                             # rewritten composition root
├── DependencyInjection.cs                 # NEW — AddBffPresentation
├── appsettings.json                       # remove JWT / DB; add Auth + Redis + Gateway sections
├── appsettings.Development.json
└── Dockerfile                             # update env vars

tests/
├── Auth.Application.UnitTests/
│   └── Admin/                             # NEW — handler + validator tests
├── Auth.API.IntegrationTests/
│   └── Endpoints/Admin/                   # admin endpoint tests with permission policy
└── Web.Blazor.IntegrationTests/           # NEW project
    ├── Web.Blazor.IntegrationTests.csproj
    ├── Infrastructure/BffWebApplicationFactory.cs   # WireMock Auth.API + WireMock Gateway + Redis testcontainer
    ├── Authentication/
    │   ├── SignInFlowTests.cs
    │   └── TokenStoreTests.cs
    └── Pages/
        └── AdminPageRenderingTests.cs     # bUnit-based smoke for permission gating
```

**Compose changes:**
- `web.blazor` service env vars updated to the new Auth/Redis/Gateway shape.
- Default port stays `5002` (matches the seeded `bff-blazor` redirect URI).

**Documentation:**
- `CLAUDE.md` §3 (Web.Blazor section), §9 (env vars), §13 (BFF pitfalls).
- `README.md` Blazor BFF section.

---

## Phase 0 — Package additions and project scaffolding

### Task 0.1: Package versions

**Files:**
- Modify: `Directory.Packages.props`

- [ ] **Step 1: Add MudBlazor + bUnit packages**

```xml
<!-- Blazor BFF -->
<PackageVersion Include="MudBlazor" Version="8.4.0" />
<PackageVersion Include="Microsoft.Extensions.Http" Version="10.0.7" />
<!-- BFF tests -->
<PackageVersion Include="bunit" Version="1.40.0" />
```

- [ ] **Step 2: Restore + verify**

```bash
dotnet restore BaseProjectScaffold.sln
```

- [ ] **Step 3: Commit**

```bash
git add Directory.Packages.props
git commit -m "chore: add MudBlazor and bUnit package versions for BFF"
```

### Task 0.2: Web.Blazor.IntegrationTests project

**Files:**
- Create: `tests/Web.Blazor.IntegrationTests/Web.Blazor.IntegrationTests.csproj`

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
    <ProjectReference Include="..\..\src\EntryPoints\Web.Blazor\Web.Blazor.csproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="bunit" />
    <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
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

- [ ] **Step 1: Add to solution + build**

```bash
dotnet sln BaseProjectScaffold.sln add tests/Web.Blazor.IntegrationTests/Web.Blazor.IntegrationTests.csproj
dotnet build BaseProjectScaffold.sln -c Release
```

Expected: 0 errors, 0 warnings.

- [ ] **Step 2: Commit**

```bash
git add tests/Web.Blazor.IntegrationTests/ BaseProjectScaffold.sln
git commit -m "test(bff): scaffold Web.Blazor integration test project"
```

---

## Phase 1 — Auth.API admin surface

The BFF needs HTTP endpoints to manage users/groups/roles/permissions/M2MClients/audit. We add them to Auth.API as a new `/admin/*` surface, gated by permission claims using a custom `PermissionRequirement`.

### Task 1.1: `PermissionRequirement` authorization

**Files:**
- Create: `src/EntryPoints/Auth.API/Authorization/PermissionRequirement.cs`
- Create: `src/EntryPoints/Auth.API/Authorization/PermissionAuthorizationHandler.cs`
- Create: `src/EntryPoints/Auth.API/Authorization/PermissionPolicyProvider.cs`
- Modify: `src/EntryPoints/Auth.API/DependencyInjection.cs` (register the policy provider)

- [ ] **Step 1: Files**

```csharp
// PermissionRequirement.cs
using Microsoft.AspNetCore.Authorization;

namespace Auth.API.Authorization;

internal sealed class PermissionRequirement(string permission) : IAuthorizationRequirement
{
    public string Permission { get; } = permission;
}
```

```csharp
// PermissionAuthorizationHandler.cs
using Microsoft.AspNetCore.Authorization;

namespace Auth.API.Authorization;

internal sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        var has = context.User.Claims.Any(c =>
            c.Type == "permission" && c.Value == requirement.Permission);
        if (has)
        {
            context.Succeed(requirement);
        }
        return Task.CompletedTask;
    }
}
```

```csharp
// PermissionPolicyProvider.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace Auth.API.Authorization;

internal sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options)
    : DefaultAuthorizationPolicyProvider(options)
{
    public const string PolicyPrefix = "permission:";

    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (!policyName.StartsWith(PolicyPrefix, StringComparison.Ordinal))
        {
            return await base.GetPolicyAsync(policyName);
        }

        var permission = policyName[PolicyPrefix.Length..];
        return new AuthorizationPolicyBuilder()
            .AddAuthenticationSchemes(
                OpenIddict.Validation.AspNetCore.OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)
            .RequireAuthenticatedUser()
            .AddRequirements(new PermissionRequirement(permission))
            .Build();
    }
}
```

In `Auth.API/DependencyInjection.cs.AddAuthApiPresentation`, after `services.AddAuthorization()`, register:

```csharp
services.AddSingleton<IAuthorizationPolicyProvider, Authorization.PermissionPolicyProvider>();
services.AddSingleton<IAuthorizationHandler, Authorization.PermissionAuthorizationHandler>();
```

- [ ] **Step 2: Build + tests**

```bash
dotnet build BaseProjectScaffold.sln -c Release
dotnet test BaseProjectScaffold.sln -c Release
```

Expected: 0 errors, all tests pass (no new tests added in this step — the policy will be exercised by Phase 1.x integration tests).

- [ ] **Step 3: Commit**

```bash
git add src/EntryPoints/Auth.API/Authorization/ src/EntryPoints/Auth.API/DependencyInjection.cs
git commit -m "feat(auth): permission-based authorization policy provider"
```

### Task 1.2 through 1.7: Admin handlers (per aggregate)

For each aggregate (Users, Groups, Roles, Permissions, M2MClients, Audit), implement the queries/commands listed in the file structure above. Each follows the existing `Auth.Application` handler pattern (private sealed record command, internal sealed validator, public sealed handler).

For brevity in this plan, the implementer should follow the spec table below. **Each task = one aggregate = one commit**.

**Task 1.2 — Users admin** (`feat(auth): admin queries and commands for users`):
- `ListUsersQuery(int Page, int PageSize, string? Search) : IQuery<PagedResponse<UserSummary>>` — paginate, optionally filter by email/displayName/IsActive.
- `GetUserByIdQuery(Guid Id) : IQuery<UserDetailResponse>` — includes assigned roles + groups (as IDs and names).
- `PreProvisionUserCommand(string Email, string DisplayName, string? NetSuiteEmail) : ICommand<Guid>` — calls `User.PreProvision`, persists.
- `DisableUserCommand(Guid Id) : ICommand`.
- `EnableUserCommand(Guid Id) : ICommand`.
- `SetNetSuiteEmailCommand(Guid Id, string? NetSuiteEmail) : ICommand` — validator: email format if non-null, max 320.
- `AssignRoleToUserCommand(Guid UserId, Guid RoleId) : ICommand`.
- `RevokeRoleFromUserCommand(Guid UserId, Guid RoleId) : ICommand`.
- `AddUserToGroupCommand(Guid UserId, Guid GroupId) : ICommand`.
- `RemoveUserFromGroupCommand(Guid UserId, Guid GroupId) : ICommand`.

Tests: 1-3 per handler covering happy + main failure path. Validator tests for the two validators.

**Task 1.3 — Groups admin** (`feat(auth): admin queries and commands for groups`):
- `ListGroupsQuery(int Page, int PageSize, string? Search) : IQuery<PagedResponse<GroupSummary>>`.
- `GetGroupByIdQuery(Guid Id) : IQuery<GroupDetailResponse>` — includes assigned roles.
- `CreateGroupCommand(string Name, string Description, Guid? EntraGroupId) : ICommand<Guid>` — validator: name non-empty, max 200; reject duplicate within tenant (`Conflict("Group.NameAlreadyTaken")`).
- `UpdateGroupCommand(Guid Id, string Name, string Description, Guid? EntraGroupId) : ICommand`.
- `DeleteGroupCommand(Guid Id) : ICommand` — soft delete (set IsDeleted).
- `AssignRoleToGroupCommand(Guid GroupId, Guid RoleId) : ICommand`.
- `RevokeRoleFromGroupCommand(Guid GroupId, Guid RoleId) : ICommand`.

**Task 1.4 — Roles admin** (`feat(auth): admin queries and commands for roles`):
- `ListRolesQuery`, `GetRoleByIdQuery`, `CreateRoleCommand`, `UpdateRoleCommand`, `DeleteRoleCommand`, `AssignPermissionToRoleCommand`, `RevokePermissionFromRoleCommand`.

**Task 1.5 — Permissions read-only** (`feat(auth): admin query for permissions`):
- `ListPermissionsQuery() : IQuery<IReadOnlyCollection<PermissionResponse>>` — returns all 9 seeded codes + descriptions.

**Task 1.6 — M2M Clients admin** (`feat(auth): admin queries and commands for M2M clients`):
- `ListM2MClientsQuery`, `GetM2MClientByIdQuery`, `CreateM2MClientCommand` (returns `(Id, ClientId, ClientSecret)` — secret shown once), `RegenerateM2MClientSecretCommand` (returns new secret), `DeactivateM2MClientCommand`.
- The `ClientSecret` is generated server-side as a base64-URL-safe random 32-byte string. Store hashed via `IClientSecretHasher`.
- Also seed an `OpenIddictApplication` mirroring this client so it can use the token endpoint. **Critical**: when a M2M client is created via this admin command, the handler must ALSO call `IOpenIddictApplicationManager.CreateAsync(...)` with `Permissions.Endpoints.Token + Permissions.GrantTypes.ClientCredentials` and the same scopes. When deactivated, the OpenIddict application is deleted as well (so existing tokens can no longer be refreshed). When the secret is rotated, the OpenIddict application's secret is updated too.

**Task 1.7 — Audit log read** (`feat(auth): admin query for audit events`):
- `ListAuditEventsQuery(int Page, int PageSize, Guid? UserId, AuthAuditEventType? EventType, DateTimeOffset? From, DateTimeOffset? To) : IQuery<PagedResponse<AuthAuditEventResponse>>`.

For each task above, after each commit run:
```bash
dotnet build BaseProjectScaffold.sln -c Release
dotnet test BaseProjectScaffold.sln -c Release
```

Both must remain green. Expected delta: ~6 commits, ~30-40 new tests.

### Task 1.8: Admin HTTP endpoints (Auth.API)

**Files:**
- Create: `src/EntryPoints/Auth.API/Endpoints/Admin/UsersEndpoints.cs`
- Create: `src/EntryPoints/Auth.API/Endpoints/Admin/GroupsEndpoints.cs`
- Create: `src/EntryPoints/Auth.API/Endpoints/Admin/RolesEndpoints.cs`
- Create: `src/EntryPoints/Auth.API/Endpoints/Admin/PermissionsEndpoints.cs`
- Create: `src/EntryPoints/Auth.API/Endpoints/Admin/M2MClientsEndpoints.cs`
- Create: `src/EntryPoints/Auth.API/Endpoints/Admin/AuditEndpoints.cs`

Each endpoint class is `internal sealed : IEndpoint`. Each `MapEndpoint` registers a route group `/admin/{aggregate}` with `.RequireAuthorization("permission:<code>")` per route.

Example shape (Users):

```csharp
internal sealed class UsersEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/admin/users").WithTags("Admin: Users");

        group.MapGet("/", async (
            int? page, int? pageSize, string? search,
            IQueryHandler<ListUsersQuery, PagedResponse<UserSummary>> handler,
            CancellationToken ct) =>
        {
            var result = await handler.Handle(new ListUsersQuery(page ?? 1, pageSize ?? 20, search), ct);
            return result.Match(Results.Ok, CustomResults.Problem);
        }).RequireAuthorization($"{PermissionPolicyProvider.PolicyPrefix}{PermissionCodes.UsersRead}");

        group.MapGet("/{id:guid}", ...).RequireAuthorization($"...{PermissionCodes.UsersRead}");
        group.MapPost("/pre-provision", ...).RequireAuthorization($"...{PermissionCodes.UsersWrite}");
        group.MapPost("/{id:guid}/disable", ...).RequireAuthorization($"...{PermissionCodes.UsersWrite}");
        // etc
    }
}
```

`CustomResults.Problem` is the existing helper from Plan 1 — verify/copy from `src/EntryPoints/Web.API/Infrastructure/CustomResults.cs` if not in Auth.API yet (likely not — copy adapted to Auth.API or share via a small `Auth.API.Infrastructure.CustomResults` class).

Tests in `tests/Auth.API.IntegrationTests/Endpoints/Admin/`:
- For each endpoint: 401 without token, 403 with token lacking permission, 200/201/204 with token bearing the right permission.

For permission-bearing tokens in tests, the `AuthWebApplicationFactory` from Plan 1 has helpers; extend it with `IssueAccessTokenAsync(string clientId, string secret, params string[] permissions)` if needed.

Run tests: `dotnet test BaseProjectScaffold.sln -c Release`. Expected: ~50-70 new integration tests.

Commit: `feat(auth): HTTP admin endpoints with permission-based authorization`

---

## Phase 2 — Web.Blazor BFF wiring

### Task 2.1: Strip existing Web.Blazor JWT/DB infrastructure

**Files:**
- Modify: `src/EntryPoints/Web.Blazor/Web.Blazor.csproj` — remove `<ProjectReference Include="..\..\Application\Application.csproj" />` and `..\..\Infra\Infra.csproj`. The BFF will not directly call Application/Infra. Add references to nothing new yet (just a clean Blazor server entrypoint).
- Modify: `src/EntryPoints/Web.Blazor/Program.cs` — strip `AddInfrastructure`, JWT bearer, EF, RabbitMQ. Keep only the Blazor server pipeline + `AddOpenTelemetryObservability(serviceName: "Web.Blazor")` + Serilog.
- Modify: `appsettings.json` — remove `Jwt`, `DB_CONNECTION_STRING`, `RabbitMq`. Add empty `Auth`, `Redis`, `Gateway` sections.

If existing Razor components (`Components/Pages/Weather.razor`) reference Application/Infra services, simplify them to local stub data. The Weather page is the .NET template's demo and can stay as an unprotected anonymous page; the new Admin pages are the real surface.

After this task, `dotnet run --project src/EntryPoints/Web.Blazor` should start a minimal Blazor server with no auth and the demo pages.

Commit: `refactor(bff): strip JWT/DB/RabbitMQ from Web.Blazor entrypoint`

### Task 2.2: Cookie + OIDC code+PKCE wiring

**Files:**
- Create: `src/EntryPoints/Web.Blazor/Authentication/BffAuthenticationExtensions.cs`
- Modify: `src/EntryPoints/Web.Blazor/Web.Blazor.csproj` — add `Microsoft.AspNetCore.Authentication.OpenIdConnect`, `Microsoft.AspNetCore.Authentication.Cookies`, `Microsoft.Extensions.Caching.StackExchangeRedis`, `Microsoft.AspNetCore.DataProtection.StackExchangeRedis`, `StackExchange.Redis`.

`BffAuthenticationExtensions.cs`:

```csharp
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Web.Blazor.Authentication;

internal static class BffAuthenticationExtensions
{
    public const string CookieScheme = "BffCookie";
    public const string OidcScheme = "BffOidc";

    public static IServiceCollection AddBffAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var section = configuration.GetSection("Auth");
        var authority = section["Authority"] ?? throw new InvalidOperationException("Auth:Authority required.");
        var clientId = section["ClientId"] ?? throw new InvalidOperationException("Auth:ClientId required.");
        var clientSecret = section["ClientSecret"] ?? throw new InvalidOperationException("Auth:ClientSecret required.");

        services.AddAuthentication(opt =>
            {
                opt.DefaultScheme = CookieScheme;
                opt.DefaultChallengeScheme = OidcScheme;
                opt.DefaultSignInScheme = CookieScheme;
            })
            .AddCookie(CookieScheme, options =>
            {
                options.Cookie.Name = "__Host-Bff-Session";
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.ExpireTimeSpan = TimeSpan.FromHours(8);
                options.SlidingExpiration = true;
                options.LoginPath = "/login";
                options.LogoutPath = "/logout";
                options.AccessDeniedPath = "/access-denied";
            })
            .AddOpenIdConnect(OidcScheme, options =>
            {
                options.Authority = authority;
                options.ClientId = clientId;
                options.ClientSecret = clientSecret;
                options.ResponseType = "code";
                options.UsePkce = true;
                options.SaveTokens = true;
                options.GetClaimsFromUserInfoEndpoint = true;
                options.Scope.Clear();
                options.Scope.Add("openid");
                options.Scope.Add("profile");
                options.Scope.Add("email");
                options.Scope.Add("offline_access");
                options.Scope.Add("api:web");
                options.CallbackPath = "/signin-oidc";
                options.SignedOutCallbackPath = "/signout-callback-oidc";
                options.RequireHttpsMetadata = false; // Dev only — production must be HTTPS
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    NameClaimType = "name",
                    RoleClaimType = "role",
                    ValidateIssuer = true
                };
                options.MapInboundClaims = false;
            });

        services.AddAuthorization();
        return services;
    }
}
```

Modify `Program.cs` to call `builder.Services.AddBffAuthentication(builder.Configuration)`, plus `app.UseAuthentication(); app.UseAuthorization();` middleware.

Update `appsettings.Development.json`:

```json
{
  "Auth": {
    "Authority": "http://localhost:5100",
    "ClientId": "bff-blazor",
    "ClientSecret": "dev-only-bff-secret-change-me"
  },
  "Redis": { "ConnectionString": "localhost:6379" },
  "Gateway": { "BaseUrl": "http://localhost:5200" }
}
```

After this task, the BFF should boot and show a Login button that redirects to Auth.API discovery / authorize.

Commit: `feat(bff): cookie + OIDC code+PKCE federated to Auth.API`

### Task 2.3: Redis-backed token store + DataProtection

**Files:**
- Create: `src/EntryPoints/Web.Blazor/Authentication/TokenStore/ITokenStore.cs`
- Create: `src/EntryPoints/Web.Blazor/Authentication/TokenStore/RedisTokenStore.cs`
- Modify: `Program.cs` (register Redis multiplexer + DataProtection persistence + ITokenStore)

```csharp
// ITokenStore.cs
namespace Web.Blazor.Authentication.TokenStore;

public sealed record SessionTokens(string AccessToken, string? RefreshToken, string? IdToken, DateTimeOffset ExpiresAt);

public interface ITokenStore
{
    Task SaveAsync(string sessionId, SessionTokens tokens, CancellationToken ct = default);
    Task<SessionTokens?> GetAsync(string sessionId, CancellationToken ct = default);
    Task RemoveAsync(string sessionId, CancellationToken ct = default);
}
```

```csharp
// RedisTokenStore.cs
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;

namespace Web.Blazor.Authentication.TokenStore;

internal sealed class RedisTokenStore(IDistributedCache cache) : ITokenStore
{
    private const string KeyPrefix = "bff:tokens:";

    public async Task SaveAsync(string sessionId, SessionTokens tokens, CancellationToken ct = default)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(tokens);
        await cache.SetAsync($"{KeyPrefix}{sessionId}", json, new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24)
        }, ct);
    }

    public async Task<SessionTokens?> GetAsync(string sessionId, CancellationToken ct = default)
    {
        var bytes = await cache.GetAsync($"{KeyPrefix}{sessionId}", ct);
        return bytes is null ? null : JsonSerializer.Deserialize<SessionTokens>(bytes);
    }

    public Task RemoveAsync(string sessionId, CancellationToken ct = default) =>
        cache.RemoveAsync($"{KeyPrefix}{sessionId}", ct);
}
```

In `Program.cs` (before `AddBffAuthentication`):

```csharp
var redisConn = builder.Configuration["Redis:ConnectionString"]
    ?? throw new InvalidOperationException("Redis:ConnectionString required.");
builder.Services.AddStackExchangeRedisCache(o => o.Configuration = redisConn);
var multiplexer = StackExchange.Redis.ConnectionMultiplexer.Connect(redisConn);
builder.Services.AddSingleton<StackExchange.Redis.IConnectionMultiplexer>(multiplexer);
builder.Services.AddDataProtection()
    .PersistKeysToStackExchangeRedis(multiplexer, "bff:dp-keys")
    .SetApplicationName("Web.Blazor.BFF");
builder.Services.AddSingleton<ITokenStore, RedisTokenStore>();
```

Hook OIDC `OnTokenValidated` in `BffAuthenticationExtensions` to:
1. Generate a session id (`Guid.NewGuid().ToString("N")`).
2. Read tokens from `context.TokenEndpointResponse` (access, refresh, id) and `context.SecurityToken`.
3. Call `ITokenStore.SaveAsync(sessionId, tokens, ...)`.
4. Add `sessionId` as a claim on the principal: `context.Principal.AddIdentity(new ClaimsIdentity([new Claim("session_id", sessionId)]))`.

This requires injecting `ITokenStore` into the OIDC options, which means `services.AddSingleton<IConfigureOptions<OpenIdConnectOptions>, ConfigureBffOidcOptions>()` to do the runtime wiring. Or the simpler path: configure inline in `BffAuthenticationExtensions` by capturing `services.BuildServiceProvider()` (anti-pattern; avoid). Use `AddOptions<OpenIdConnectOptions>(OidcScheme).Configure<ITokenStore>((opt, store) => { opt.Events.OnTokenValidated = ctx => { ... }; })`.

Commit: `feat(bff): Redis-backed token store keyed by session id`

### Task 2.4: Logout flow

**Files:**
- Modify: `BffAuthenticationExtensions.cs` (set up OIDC `OnSignedOutCallbackRedirect` and signout flow)
- Add a small endpoint in `Program.cs`: `app.MapPost("/logout", ...)` — signs out cookie + OIDC, removes token store entry.

Logout: receives session_id from claim → `ITokenStore.RemoveAsync` → `HttpContext.SignOutAsync(CookieScheme)` → `HttpContext.SignOutAsync(OidcScheme, new AuthenticationProperties { RedirectUri = "/" })`.

Commit: `feat(bff): logout flow clears session tokens and signs out OIDC`

---

## Phase 3 — Gateway client

### Task 3.1: `IAdminGatewayClient`

**Files:**
- Create: `src/EntryPoints/Web.Blazor/Gateway/IAdminGatewayClient.cs`
- Create: `src/EntryPoints/Web.Blazor/Gateway/AdminGatewayClient.cs`

`IAdminGatewayClient` exposes typed methods matching the Auth.API admin endpoints. For brevity:

```csharp
public interface IAdminGatewayClient
{
    Task<PagedResponse<UserSummary>> ListUsersAsync(int page, int pageSize, string? search, CancellationToken ct);
    Task<UserDetailResponse?> GetUserAsync(Guid id, CancellationToken ct);
    Task<Guid> PreProvisionUserAsync(string email, string displayName, string? netSuiteEmail, CancellationToken ct);
    Task DisableUserAsync(Guid id, CancellationToken ct);
    Task EnableUserAsync(Guid id, CancellationToken ct);
    Task SetNetSuiteEmailAsync(Guid id, string? netSuiteEmail, CancellationToken ct);
    Task AssignRoleToUserAsync(Guid userId, Guid roleId, CancellationToken ct);
    // ... groups, roles, permissions, m2mclients, audit
}
```

The DTOs (`UserSummary`, `UserDetailResponse`, etc.) should mirror the Auth.Application admin response records. Define them in `src/EntryPoints/Web.Blazor/Gateway/Contracts/` as a copy (don't ProjectReference Auth.Application).

`AdminGatewayClient` is a `HttpClient`-based impl. Each method:
1. Fetches the access token via `IHttpContextAccessor` → session_id claim → `ITokenStore.GetAsync`.
2. Sets `Authorization: Bearer <accessToken>`.
3. Calls `_httpClient.GetFromJsonAsync<>` / `PostAsJsonAsync` etc. against `Gateway:BaseUrl + /api/auth/admin/...`.

Wait — the Gateway routes `/api/auth/connect/*` to Auth.API; admin endpoints are at `/admin/*` on Auth.API. Add a YARP route `auth-admin` to the Gateway: `Path: /api/auth/admin/{**catch-all}` → cluster `auth-cluster`, transform `PathRemovePrefix: /api/auth`, **AuthorizationPolicy: RequireBearer**. This is a Gateway change; include it in the BFF plan as Task 3.2.

Register `IAdminGatewayClient` via `AddHttpClient<IAdminGatewayClient, AdminGatewayClient>(c => c.BaseAddress = new Uri(configuration["Gateway:BaseUrl"]!));`.

Commit: `feat(bff): typed AdminGatewayClient for Auth.API admin endpoints`

### Task 3.2: Add `auth-admin` route to Gateway

**Files:**
- Modify: `src/EntryPoints/Gateway/appsettings.Development.json` — append:

```json
"auth-admin": {
  "ClusterId": "auth-cluster",
  "Match": { "Path": "/api/auth/admin/{**catch-all}" },
  "AuthorizationPolicy": "RequireBearer",
  "Transforms": [
    { "PathRemovePrefix": "/api/auth" }
  ]
}
```

- Modify: `compose.yaml` Gateway service env vars — add `ReverseProxy__Routes__auth-admin__*` keys mirroring the JSON.

Run integration tests; the existing Gateway tests should still pass. Add a new Gateway test:
- `AdminRoute_RequiresAuth_ReturnsForwardedAdminCall`: POST a valid bearer token to `/api/auth/admin/users` → assert WireMock Auth.API stub at `/admin/users` was called with `Authorization` header.

Commit: `feat(gateway): route /api/auth/admin/* to Auth.API with bearer auth`

---

## Phase 4 — Razor pages

### Task 4.1: MudBlazor + layout

**Files:**
- Modify: `src/EntryPoints/Web.Blazor/Web.Blazor.csproj` — add `MudBlazor` package reference.
- Modify: `src/EntryPoints/Web.Blazor/Program.cs` — `builder.Services.AddMudServices()`.
- Modify: `Components/App.razor` — add MudBlazor's `<HeadContent>` for fonts/icons; wrap with `<MudThemeProvider />`, `<MudPopoverProvider />`, `<MudDialogProvider />`, `<MudSnackbarProvider />`.
- Modify: `Components/Layout/MainLayout.razor` — convert to `MudLayout` with `MudAppBar` (logo + user menu) and `MudDrawer` for navigation.
- Modify: `Components/Layout/NavMenu.razor` — replace template links with admin nav (Users, Groups, Roles, Permissions, M2M, Audit) gated by permission claims using `<PermissionView Permission="users.read">...</PermissionView>` (added in Task 4.2).
- Create: `Components/Layout/LoginButton.razor` — shows "Login" if not authenticated (links to `/login`), or user info + logout if authenticated.

Commit: `feat(bff): MudBlazor theme and authenticated layout`

### Task 4.2: `PermissionView` component

**Files:**
- Create: `src/EntryPoints/Web.Blazor/Components/Shared/PermissionView.razor`

```razor
@inject AuthenticationStateProvider AuthState

@if (_hasPermission)
{
    @ChildContent
}
else if (NoAccessFallback is not null)
{
    @NoAccessFallback
}

@code {
    [Parameter] public string Permission { get; set; } = "";
    [Parameter] public RenderFragment? ChildContent { get; set; }
    [Parameter] public RenderFragment? NoAccessFallback { get; set; }

    private bool _hasPermission;

    protected override async Task OnInitializedAsync()
    {
        var state = await AuthState.GetAuthenticationStateAsync();
        _hasPermission = state.User.Claims.Any(c => c.Type == "permission" && c.Value == Permission);
    }
}
```

Commit: `feat(bff): PermissionView component for claim-gated UI`

### Task 4.3 through 4.10: Admin pages

For each aggregate, implement list + detail pages using MudBlazor `MudDataGrid`, `MudDialog` for forms, `MudSnackbar` for feedback.

Each page is gated with `[Authorize]` attribute + `<PermissionView Permission="X.read">` wrapping the content.

**Task 4.3: Users list + detail** (`feat(bff): users admin pages`):
- `Pages/Admin/Users.razor`: paginated table, filter by email/active. Action menu: View, Disable, Enable, Pre-provision.
- `Pages/Admin/UserDetail.razor`: detail card, assigned roles + groups, "Disable", "Set NetSuite email" (dialog), "Assign role" (dialog).

**Task 4.4: Groups** (`feat(bff): groups admin pages`).
**Task 4.5: Roles** (`feat(bff): roles admin pages`).
**Task 4.6: Permissions read-only** (`feat(bff): permissions list page`).
**Task 4.7: M2M Clients** (`feat(bff): M2M clients admin pages`):
- Create dialog generates secret, shows once with copy-to-clipboard, then never again (alert: "store this secret now").
**Task 4.8: Audit log** (`feat(bff): audit log page`):
- Filter by user, event type, date range. `MudDataGrid` with server-side paging.

Each task: 1 commit. Total ~6 commits.

After this phase the BFF is functional end-to-end against the local stack.

---

## Phase 5 — Tests

### Task 5.1: `BffWebApplicationFactory`

**File:** `tests/Web.Blazor.IntegrationTests/Infrastructure/BffWebApplicationFactory.cs`

Mirrors `AuthWebApplicationFactory` from Plan 1: spawns Redis testcontainer, WireMock for Auth.API (discovery, authorize, token, admin endpoints), WireMock for Gateway (passthrough to WireMock Auth.API). Overrides `WebApplicationFactory<Web.Blazor.Program>.ConfigureWebHost`.

Commit: `test(bff): WebApplicationFactory with Redis + WireMock Auth.API + WireMock Gateway`

### Task 5.2: Sign-in flow tests

**File:** `tests/Web.Blazor.IntegrationTests/Authentication/SignInFlowTests.cs`

Tests:
- `LoginEndpoint_RedirectsToOidcChallenge`: `GET /login` → 302 Location starts with WireMock Auth.API authorize URL.
- `OidcCallback_PersistsTokensAndSetsCookie`: simulate the full code-exchange, assert token stored in Redis under session_id, cookie set.
- `Logout_ClearsTokensAndSignsOut`: POST /logout → cookie cleared, Redis entry removed.

Commit: `test(bff): sign-in and logout integration tests`

### Task 5.3: Token store unit tests

**File:** `tests/Web.Blazor.IntegrationTests/Authentication/TokenStoreTests.cs`

Use `MemoryDistributedCache` to test save/get/remove + JSON serialization.

Commit: `test(bff): RedisTokenStore unit tests`

### Task 5.4: Permission-gated UI tests (bUnit)

**File:** `tests/Web.Blazor.IntegrationTests/Pages/AdminPageRenderingTests.cs`

Use bUnit to render admin pages with mocked `AuthenticationStateProvider`. Assert:
- Without permission: page shows "access denied" or empty.
- With permission: page shows MudDataGrid.

Commit: `test(bff): bUnit tests for permission-gated admin pages`

---

## Phase 6 — Compose, Dockerfile, docs

### Task 6.1: Update Web.Blazor compose service

**File:** `compose.yaml`

Update `web.blazor` env vars: replace `JWT_SECRET`, `DB_CONNECTION_STRING`, `RABBITMQ_*` with:

```yaml
- ASPNETCORE_ENVIRONMENT=Development
- Auth__Authority=http://auth.api:8080
- Auth__ClientId=bff-blazor
- Auth__ClientSecret=${OPENIDDICT_BFF_SECRET:-dev-only-bff-secret-change-me}
- Redis__ConnectionString=redis:6379
- Gateway__BaseUrl=http://gateway:8080
```

Update `depends_on` to `[redis, auth.api, gateway]`. Remove `postgres`, `rabbitmq` if unused. (If the existing scaffold's Web.API workflow still uses them, keep — but Web.Blazor itself doesn't need them anymore.)

Smoke: `docker compose up -d redis auth-postgres auth.api gateway web.blazor`. Browse `http://localhost:5002` → should show Login button. Click → redirected through Entra (or stuck on Entra real auth — for compose smoke, having the BFF reach /signin-oidc is sufficient proof). `docker compose down`.

Commit: `chore(bff): update Web.Blazor compose env vars for OIDC + Redis + Gateway`

### Task 6.2: Update Web.Blazor Dockerfile

**File:** `src/EntryPoints/Web.Blazor/Dockerfile`

Verify the existing Dockerfile still builds with the new dependency graph (no Application/Infra ProjectReferences). Adjust COPY lines if needed.

Commit: `chore(bff): update Web.Blazor Dockerfile after JWT/EF removal`

### Task 6.3: Documentation

**Files:**
- Modify: `CLAUDE.md` — §3 (Web.Blazor row updated), §9 (env vars), §13 (BFF pitfalls: OIDC RequireHttpsMetadata=false dev only, session_id claim, etc.).
- Modify: `README.md` — Blazor BFF section EN + PT.

Commit: `docs: document Blazor BFF in CLAUDE.md and README`

---

## Final verification

- [ ] **Step 1: Build + test**

```bash
dotnet build BaseProjectScaffold.sln -c Release
dotnet test BaseProjectScaffold.sln -c Release
```

Expected: 0 errors, 0 warnings. Test count delta ~50-80 from Phase 1 admin handlers + Phase 5 BFF tests.

- [ ] **Step 2: End-to-end smoke**

```bash
docker compose up -d --build redis auth-postgres auth.api gateway web.blazor
sleep 25
curl -sI http://localhost:5002/login   # expect 302 to Auth.API/connect/authorize?...
docker compose down
```

- [ ] **Step 3: Tag**

```bash
git tag -a bff-v1 -m "Blazor BFF complete (Plan 3 of 5)"
```

---

## Self-review

1. **Spec coverage** — Plan 3 covers spec §7.1 (browser login flow), §9 (admin pages), §10 (env vars added). Out of scope: §7.3 NetSuite SAML (Plan 4), §8 Web.API integration (Plan 5).

2. **Placeholder scan** — Tasks 1.2 through 1.7 are described in dense table form (one paragraph per aggregate) rather than full code blocks. The implementer is expected to follow the Plan 1 handler pattern exactly. If this proves too thin, the implementer should expand inline.

3. **Type consistency** — `SessionTokens(AccessToken, RefreshToken, IdToken, ExpiresAt)` consistent across `ITokenStore` and `RedisTokenStore`. `IAdminGatewayClient` methods match Auth.Application command/query contract types.

4. **Spec items with no task** — `OpenIddictApplication` lifecycle for M2M clients is now in Phase 1.6 (created/updated/deleted in lockstep with the domain entity). Audit retention policy is documented but not enforced (V1 keeps all rows).

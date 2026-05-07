# Auth.API Core Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stand up the standalone identity service (`Auth.API`) with multi-tenant data model, Entra ID federation, JIT user provisioning, OpenIddict-based OIDC server, M2M `client_credentials` grant, opaque reference tokens with `/connect/introspect`, and Redis-backed token store — all gated by xUnit/Shouldly tests and NetArchTest architecture rules.

**Architecture:** New bounded context `Auth.*` (Domain → Application → Infra → Auth.API entrypoint), dedicated PostgreSQL database `auth_db`, Redis for OpenIddict + introspection cache. OIDC code+PKCE upstream to Entra; opaque access/refresh tokens issued downstream. Tenant resolved from Entra `tid` claim. Permission claims materialized at token-issuance time.

**Tech Stack:** .NET 10, OpenIddict 6.x (`OpenIddict.AspNetCore`, `OpenIddict.EntityFrameworkCore`, `OpenIddict.Validation.AspNetCore`), EF Core 10 + Npgsql, StackExchange.Redis, FluentValidation 12, Scrutor, xUnit, Shouldly, Moq, WireMock.Net, NetArchTest, Serilog, OpenTelemetry.

**Out of scope (other plans):** Gateway YARP (Plan 2), Blazor BFF (Plan 3), NetSuite SAML (Plan 4), Web.API integration (Plan 5).

---

## File Structure

```
src/
├── Auth.Domain/Auth.Domain.csproj
│   ├── Tenants/Tenant.cs, TenantErrors.cs
│   ├── Users/User.cs, UserErrors.cs
│   ├── Groups/Group.cs, GroupErrors.cs
│   ├── Roles/Role.cs, RoleErrors.cs
│   ├── Permissions/Permission.cs, PermissionCodes.cs
│   ├── M2MClients/M2MClient.cs, M2MClientErrors.cs
│   └── Audit/AuthAuditEvent.cs, AuthAuditEventType.cs
│
├── Auth.Application/Auth.Application.csproj
│   ├── Abstractions/
│   │   ├── Data/IAuthDbContext.cs
│   │   ├── Tenancy/ITenantContext.cs
│   │   ├── Identity/IPermissionResolver.cs
│   │   └── Crypto/IClientSecretHasher.cs
│   ├── Tenants/Resolve/ResolveTenantQuery.cs (+Handler)
│   ├── Users/SyncEntra/SyncEntraUserCommand.cs (+Validator+Handler)
│   ├── Users/GetEffectivePermissions/GetEffectivePermissionsQuery.cs (+Handler)
│   ├── M2MClients/Authenticate/AuthenticateM2MClientCommand.cs (+Handler)
│   └── DependencyInjection.cs
│
├── Auth.Infra/Auth.Infra.csproj
│   ├── Database/AuthDbContext.cs, Schemas.cs
│   ├── Database/Migrations/ (generated)
│   ├── Config/TenantConfiguration.cs, UserConfiguration.cs, ...
│   ├── Identity/PermissionResolver.cs
│   ├── Identity/Pbkdf2ClientSecretHasher.cs
│   ├── Tenancy/TenantContext.cs (HttpContext-backed)
│   ├── OpenIddict/OpenIddictExtensions.cs
│   └── DependencyInjection.cs
│
└── EntryPoints/Auth.API/Auth.API.csproj
    ├── Program.cs
    ├── DependencyInjection.cs
    ├── appsettings.json, appsettings.Development.json
    ├── Dockerfile
    ├── Endpoints/
    │   ├── IEndpoint.cs (copied pattern)
    │   ├── Authorize/AuthorizeEndpoint.cs
    │   ├── Token/TokenEndpoint.cs
    │   ├── UserInfo/UserInfoEndpoint.cs
    │   ├── Introspection/IntrospectionEndpoint.cs
    │   ├── Revocation/RevocationEndpoint.cs
    │   └── EntraCallback/EntraCallbackEndpoint.cs
    ├── Authentication/EntraAuthenticationOptions.cs, EntraOidcHandler.cs
    └── Extensions/AuthEndpointExtensions.cs

tests/
├── Auth.Domain.UnitTests/Auth.Domain.UnitTests.csproj
│   └── per-entity tests
├── Auth.Application.UnitTests/Auth.Application.UnitTests.csproj
│   ├── Tenants/, Users/, M2MClients/, Identity/
└── Auth.API.IntegrationTests/Auth.API.IntegrationTests.csproj
    ├── Architecture/ArchitectureTests.cs
    ├── Endpoints/ (OIDC + introspection)
    ├── Infrastructure/AuthWebApplicationFactory.cs (PostgreSQL Testcontainers + WireMock for Entra)
```

**Database:** `auth_db` is a separate PostgreSQL database from `app_db`. Schema convention reuses `EFCore.NamingConventions` (snake_case).

---

## Phase 0 — Project scaffolding & solution wiring

### Task 0.1: Add new package versions to `Directory.Packages.props`

**Files:**
- Modify: `Directory.Packages.props`

- [ ] **Step 1: Add OpenIddict + Redis + WireMock + Testcontainers package versions**

Inside the existing `<ItemGroup>` add (alphabetical groups OK):

```xml
<!-- Identity -->
<PackageVersion Include="OpenIddict.AspNetCore" Version="6.4.0" />
<PackageVersion Include="OpenIddict.EntityFrameworkCore" Version="6.4.0" />
<PackageVersion Include="OpenIddict.Validation.AspNetCore" Version="6.4.0" />
<PackageVersion Include="OpenIddict.Quartz" Version="6.4.0" />
<PackageVersion Include="Quartz.Extensions.Hosting" Version="3.13.1" />
<!-- Redis -->
<PackageVersion Include="StackExchange.Redis" Version="2.8.16" />
<PackageVersion Include="Microsoft.Extensions.Caching.StackExchangeRedis" Version="10.0.7" />
<!-- Tests -->
<PackageVersion Include="WireMock.Net" Version="1.6.7" />
<PackageVersion Include="Testcontainers.PostgreSql" Version="4.0.0" />
<PackageVersion Include="Testcontainers.Redis" Version="4.0.0" />
```

- [ ] **Step 2: Verify `dotnet restore` does not break existing solution**

Run: `dotnet restore BaseProjectScaffold.sln`
Expected: restore succeeds (no projects use the new packages yet).

- [ ] **Step 3: Commit**

```bash
git add Directory.Packages.props
git commit -m "chore: add OpenIddict, Redis, WireMock, Testcontainers package versions"
```

### Task 0.2: Create empty `Auth.Domain` project

**Files:**
- Create: `src/Auth.Domain/Auth.Domain.csproj`

- [ ] **Step 1: Create the project file**

`src/Auth.Domain/Auth.Domain.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>Auth.Domain</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\SharedKernel\SharedKernel.csproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="SonarAnalyzer.CSharp">
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
  </ItemGroup>
  <ItemGroup>
    <InternalsVisibleTo Include="Auth.Application" />
    <InternalsVisibleTo Include="Auth.Infra" />
    <InternalsVisibleTo Include="Auth.Domain.UnitTests" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Add to solution**

Run: `dotnet sln BaseProjectScaffold.sln add src/Auth.Domain/Auth.Domain.csproj`
Expected: `Project ... added to the solution.`

- [ ] **Step 3: Verify build**

Run: `dotnet build src/Auth.Domain/Auth.Domain.csproj`
Expected: build succeeds (empty project).

- [ ] **Step 4: Commit**

```bash
git add src/Auth.Domain/ BaseProjectScaffold.sln
git commit -m "feat(auth): scaffold Auth.Domain project"
```

### Task 0.3: Create empty `Auth.Application` project

**Files:**
- Create: `src/Auth.Application/Auth.Application.csproj`

- [ ] **Step 1: Project file**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>Auth.Application</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\SharedKernel\SharedKernel.csproj" />
    <ProjectReference Include="..\Auth.Domain\Auth.Domain.csproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="FluentValidation" />
    <PackageReference Include="FluentValidation.DependencyInjectionExtensions" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" />
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" />
    <PackageReference Include="Scrutor" />
    <PackageReference Include="SonarAnalyzer.CSharp">
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
  </ItemGroup>
  <ItemGroup>
    <InternalsVisibleTo Include="Auth.Infra" />
    <InternalsVisibleTo Include="Auth.API" />
    <InternalsVisibleTo Include="Auth.Application.UnitTests" />
    <InternalsVisibleTo Include="Auth.API.IntegrationTests" />
  </ItemGroup>
</Project>
```

(Note: `Microsoft.Extensions.DependencyInjection.Abstractions` is transitive via existing packages; if `dotnet build` complains add it to `Directory.Packages.props` at version `10.0.7`.)

- [ ] **Step 2: Add to solution + build**

```bash
dotnet sln BaseProjectScaffold.sln add src/Auth.Application/Auth.Application.csproj
dotnet build src/Auth.Application/Auth.Application.csproj
```

Expected: success.

- [ ] **Step 3: Commit**

```bash
git add src/Auth.Application/ BaseProjectScaffold.sln Directory.Packages.props
git commit -m "feat(auth): scaffold Auth.Application project"
```

### Task 0.4: Create empty `Auth.Infra` project

**Files:**
- Create: `src/Auth.Infra/Auth.Infra.csproj`

- [ ] **Step 1: Project file**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>Auth.Infra</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\SharedKernel\SharedKernel.csproj" />
    <ProjectReference Include="..\Auth.Domain\Auth.Domain.csproj" />
    <ProjectReference Include="..\Auth.Application\Auth.Application.csproj" />
    <ProjectReference Include="..\Infra\Infra.csproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="EFCore.NamingConventions" />
    <PackageReference Include="Microsoft.AspNetCore.Http.Abstractions" />
    <PackageReference Include="Microsoft.EntityFrameworkCore" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Tools" />
    <PackageReference Include="Microsoft.Extensions.Caching.StackExchangeRedis" />
    <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" />
    <PackageReference Include="Quartz.Extensions.Hosting" />
    <PackageReference Include="OpenIddict.AspNetCore" />
    <PackageReference Include="OpenIddict.EntityFrameworkCore" />
    <PackageReference Include="OpenIddict.Quartz" />
    <PackageReference Include="StackExchange.Redis" />
    <PackageReference Include="SonarAnalyzer.CSharp">
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
  </ItemGroup>
  <ItemGroup>
    <InternalsVisibleTo Include="Auth.API" />
    <InternalsVisibleTo Include="Auth.API.IntegrationTests" />
  </ItemGroup>
</Project>
```

The `..\Infra\Infra.csproj` reference is intentional — we reuse `Infra.Observability.OpenTelemetryExtensions` and `Infra.Extensions.ConfigurationExtensions`. If the architecture test later flags this, we'll move shared bits to a new `Infra.Shared` project. Document this trade-off in PR.

- [ ] **Step 2: Add to solution + build**

```bash
dotnet sln BaseProjectScaffold.sln add src/Auth.Infra/Auth.Infra.csproj
dotnet build src/Auth.Infra/Auth.Infra.csproj
```

Expected: success.

- [ ] **Step 3: Commit**

```bash
git add src/Auth.Infra/ BaseProjectScaffold.sln
git commit -m "feat(auth): scaffold Auth.Infra project"
```

### Task 0.5: Create empty `Auth.API` entrypoint

**Files:**
- Create: `src/EntryPoints/Auth.API/Auth.API.csproj`
- Create: `src/EntryPoints/Auth.API/Program.cs`
- Create: `src/EntryPoints/Auth.API/appsettings.json`
- Create: `src/EntryPoints/Auth.API/appsettings.Development.json`

- [ ] **Step 1: Project file**

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>Auth.API</RootNamespace>
    <UserSecretsId>auth-api-2026-05-06</UserSecretsId>
    <DockerDefaultTargetOS>Linux</DockerDefaultTargetOS>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\Auth.Application\Auth.Application.csproj" />
    <ProjectReference Include="..\..\Auth.Infra\Auth.Infra.csproj" />
    <ProjectReference Include="..\..\SharedKernel\SharedKernel.csproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="AspNetCore.HealthChecks.NpgSql" />
    <PackageReference Include="AspNetCore.HealthChecks.UI.Client" />
    <PackageReference Include="Microsoft.AspNetCore.Authentication.JwtBearer" />
    <PackageReference Include="Microsoft.AspNetCore.OpenApi" />
    <PackageReference Include="Serilog.AspNetCore" />
    <PackageReference Include="Serilog.Sinks.Seq" />
    <PackageReference Include="Swashbuckle.AspNetCore" />
    <PackageReference Include="SonarAnalyzer.CSharp">
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Minimal Program.cs**

```csharp
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
app.MapGet("/health/live", () => Results.Ok(new { status = "live" }));
app.Run();

namespace Auth.API
{
    public partial class Program;
}
```

- [ ] **Step 3: appsettings.json (empty secrets, env-var driven)**

```json
{
  "Logging": { "LogLevel": { "Default": "Information" } },
  "AllowedHosts": "*",
  "ConnectionStrings": { "AuthDb": "" },
  "Redis": { "ConnectionString": "" },
  "Entra": { "TenantId": "", "ClientId": "", "ClientSecret": "", "Authority": "" },
  "OpenIddict": {
    "Issuer": "",
    "SigningCertificatePath": "",
    "EncryptionCertificatePath": "",
    "AccessTokenLifetimeSeconds": 900,
    "RefreshTokenLifetimeSeconds": 1209600
  }
}
```

`appsettings.Development.json`: same shape, `Logging.LogLevel.Microsoft.AspNetCore` set to `Warning`, leave secrets empty.

- [ ] **Step 4: Add to solution + build + smoke**

```bash
dotnet sln BaseProjectScaffold.sln add src/EntryPoints/Auth.API/Auth.API.csproj
dotnet build src/EntryPoints/Auth.API/Auth.API.csproj
```

Expected: build succeeds. (We'll smoke-run after Phase 4.)

- [ ] **Step 5: Commit**

```bash
git add src/EntryPoints/Auth.API/ BaseProjectScaffold.sln
git commit -m "feat(auth): scaffold Auth.API entrypoint with health/live"
```

### Task 0.6: Create test projects

**Files:**
- Create: `tests/Auth.Domain.UnitTests/Auth.Domain.UnitTests.csproj`
- Create: `tests/Auth.Application.UnitTests/Auth.Application.UnitTests.csproj`
- Create: `tests/Auth.API.IntegrationTests/Auth.API.IntegrationTests.csproj`

- [ ] **Step 1: `Auth.Domain.UnitTests.csproj`**

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
    <ProjectReference Include="..\..\src\Auth.Domain\Auth.Domain.csproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="Shouldly" />
    <PackageReference Include="coverlet.collector" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: `Auth.Application.UnitTests.csproj`** — same shape, references `Auth.Application` and `Auth.Domain`, plus `Moq` and `Microsoft.EntityFrameworkCore.InMemory`.

- [ ] **Step 3: `Auth.API.IntegrationTests.csproj`** — references `Auth.API`, `Auth.Application`, `Auth.Domain`, `Auth.Infra`. Add packages: `Microsoft.AspNetCore.Mvc.Testing`, `Microsoft.NET.Test.Sdk`, `xunit`, `xunit.runner.visualstudio`, `Shouldly`, `Moq`, `NetArchTest.Rules`, `WireMock.Net`, `Testcontainers.PostgreSql`, `Testcontainers.Redis`, `Microsoft.EntityFrameworkCore.InMemory`.

- [ ] **Step 4: Add all three to solution and build**

```bash
dotnet sln BaseProjectScaffold.sln add tests/Auth.Domain.UnitTests/Auth.Domain.UnitTests.csproj
dotnet sln BaseProjectScaffold.sln add tests/Auth.Application.UnitTests/Auth.Application.UnitTests.csproj
dotnet sln BaseProjectScaffold.sln add tests/Auth.API.IntegrationTests/Auth.API.IntegrationTests.csproj
dotnet build BaseProjectScaffold.sln
```

Expected: success.

- [ ] **Step 5: Commit**

```bash
git add tests/Auth.* BaseProjectScaffold.sln
git commit -m "test(auth): scaffold Auth test projects"
```

---

## Phase 1 — Domain entities (TDD)

Each entity follows the same pattern: write a failing test, write the entity, watch it pass, commit. Entities inherit the existing `Entity` base from `SharedKernel` (which provides `CreatedAt`, `DeletedAt`, `IsDeleted`).

### Task 1.1: `Tenant` entity

**Files:**
- Create: `tests/Auth.Domain.UnitTests/Tenants/TenantTests.cs`
- Create: `src/Auth.Domain/Tenants/Tenant.cs`
- Create: `src/Auth.Domain/Tenants/TenantErrors.cs`

- [ ] **Step 1: Write failing test**

`tests/Auth.Domain.UnitTests/Tenants/TenantTests.cs`:

```csharp
using Auth.Domain.Tenants;
using Shouldly;

namespace Auth.Domain.UnitTests.Tenants;

public sealed class TenantTests
{
    [Fact]
    public void Create_ShouldInitializeActiveTenantWithEntraTenantId()
    {
        var entraTenantId = Guid.NewGuid();

        var tenant = Tenant.Create(entraTenantId, "Acme Corp", "https://acme.local/signin-oidc");

        tenant.Id.ShouldNotBe(Guid.Empty);
        tenant.EntraTenantId.ShouldBe(entraTenantId);
        tenant.DisplayName.ShouldBe("Acme Corp");
        tenant.IsActive.ShouldBeTrue();
        tenant.DefaultRedirectUri.ShouldBe("https://acme.local/signin-oidc");
    }

    [Fact]
    public void Deactivate_ShouldFlipIsActiveToFalse()
    {
        var tenant = Tenant.Create(Guid.NewGuid(), "Acme", "https://x");
        tenant.Deactivate();
        tenant.IsActive.ShouldBeFalse();
    }
}
```

- [ ] **Step 2: Run, expect failure**

```bash
dotnet test tests/Auth.Domain.UnitTests/Auth.Domain.UnitTests.csproj
```

Expected: compile error (`Tenant` does not exist).

- [ ] **Step 3: Implement `Tenant`**

`src/Auth.Domain/Tenants/Tenant.cs`:

```csharp
using SharedKernel;

namespace Auth.Domain.Tenants;

public sealed class Tenant : Entity
{
    private Tenant() { }

    public Guid EntraTenantId { get; private set; }
    public string DisplayName { get; private set; } = null!;
    public string DefaultRedirectUri { get; private set; } = null!;
    public bool IsActive { get; private set; }

    public static Tenant Create(Guid entraTenantId, string displayName, string defaultRedirectUri)
    {
        return new Tenant
        {
            Id = Guid.NewGuid(),
            EntraTenantId = entraTenantId,
            DisplayName = displayName,
            DefaultRedirectUri = defaultRedirectUri,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    public void Deactivate() => IsActive = false;
    public void Activate() => IsActive = true;
}
```

`src/Auth.Domain/Tenants/TenantErrors.cs`:

```csharp
using SharedKernel;

namespace Auth.Domain.Tenants;

public static class TenantErrors
{
    public static Error NotRegistered(Guid entraTenantId) =>
        Error.NotFound("Tenant.NotRegistered", $"No tenant is registered for Entra tenant {entraTenantId}.");

    public static readonly Error Inactive =
        Error.Forbidden("Tenant.Inactive", "Tenant is not active.");
}
```

- [ ] **Step 4: Run, expect pass**

```bash
dotnet test tests/Auth.Domain.UnitTests/Auth.Domain.UnitTests.csproj
```

Expected: 2 passed.

- [ ] **Step 5: Commit**

```bash
git add src/Auth.Domain/Tenants/ tests/Auth.Domain.UnitTests/Tenants/
git commit -m "feat(auth): add Tenant entity"
```

### Task 1.2: `User` entity (multi-tenant aware)

**Files:**
- Create: `tests/Auth.Domain.UnitTests/Users/UserTests.cs`
- Create: `src/Auth.Domain/Users/User.cs`
- Create: `src/Auth.Domain/Users/UserErrors.cs`

- [ ] **Step 1: Write failing tests**

```csharp
using Auth.Domain.Users;
using Shouldly;

namespace Auth.Domain.UnitTests.Users;

public sealed class UserTests
{
    [Fact]
    public void ProvisionFromEntra_ShouldCreateActiveUserBoundToTenant()
    {
        var tenantId = Guid.NewGuid();
        var entraOid = Guid.NewGuid();

        var user = User.ProvisionFromEntra(tenantId, entraOid, "diego@acme.local", "Diego Modesto");

        user.TenantId.ShouldBe(tenantId);
        user.EntraOid.ShouldBe(entraOid);
        user.Email.ShouldBe("diego@acme.local");
        user.DisplayName.ShouldBe("Diego Modesto");
        user.IsActive.ShouldBeTrue();
        user.IsPreProvisioned.ShouldBeFalse();
        user.NetSuiteEmail.ShouldBeNull();
    }

    [Fact]
    public void PreProvision_ShouldCreateInactiveUserUntilFirstLogin()
    {
        var user = User.PreProvision(Guid.NewGuid(), "x@acme.local", "X Y");

        user.IsActive.ShouldBeFalse();
        user.IsPreProvisioned.ShouldBeTrue();
        user.EntraOid.ShouldBeNull();
    }

    [Fact]
    public void ActivateFromEntra_ShouldBindEntraOidAndActivate()
    {
        var user = User.PreProvision(Guid.NewGuid(), "x@acme.local", "X Y");
        var entraOid = Guid.NewGuid();

        user.ActivateFromEntra(entraOid, "X Y");

        user.EntraOid.ShouldBe(entraOid);
        user.IsActive.ShouldBeTrue();
        user.IsPreProvisioned.ShouldBeFalse();
    }

    [Fact]
    public void Disable_ShouldSetIsActiveFalse()
    {
        var user = User.ProvisionFromEntra(Guid.NewGuid(), Guid.NewGuid(), "a@b", "A B");
        user.Disable();
        user.IsActive.ShouldBeFalse();
    }
}
```

- [ ] **Step 2: Run, expect failure** (compile errors).

- [ ] **Step 3: Implement `User`**

```csharp
using SharedKernel;

namespace Auth.Domain.Users;

public sealed class User : Entity
{
    private User() { }

    public Guid TenantId { get; private set; }
    public Guid? EntraOid { get; private set; }
    public string Email { get; private set; } = null!;
    public string DisplayName { get; private set; } = null!;
    public string? NetSuiteEmail { get; private set; }
    public bool IsActive { get; private set; }
    public bool IsPreProvisioned { get; private set; }
    public DateTimeOffset? LastLoginAt { get; private set; }

    public static User ProvisionFromEntra(Guid tenantId, Guid entraOid, string email, string displayName) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EntraOid = entraOid,
            Email = email,
            DisplayName = displayName,
            IsActive = true,
            IsPreProvisioned = false,
            CreatedAt = DateTimeOffset.UtcNow
        };

    public static User PreProvision(Guid tenantId, string email, string displayName) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Email = email,
            DisplayName = displayName,
            IsActive = false,
            IsPreProvisioned = true,
            CreatedAt = DateTimeOffset.UtcNow
        };

    public void ActivateFromEntra(Guid entraOid, string displayName)
    {
        EntraOid = entraOid;
        DisplayName = displayName;
        IsActive = true;
        IsPreProvisioned = false;
    }

    public void Disable() => IsActive = false;
    public void Enable() => IsActive = true;
    public void SetNetSuiteEmail(string? netSuiteEmail) => NetSuiteEmail = netSuiteEmail;
    public void RecordLogin() => LastLoginAt = DateTimeOffset.UtcNow;
}
```

`UserErrors.cs`:

```csharp
using SharedKernel;

namespace Auth.Domain.Users;

public static class UserErrors
{
    public static Error NotFound(Guid id) =>
        Error.NotFound("User.NotFound", $"User '{id}' was not found.");
    public static readonly Error Disabled =
        Error.Forbidden("User.Disabled", "User is disabled.");
    public static readonly Error NetSuiteEmailMissing =
        Error.Validation("User.NetSuiteEmailMissing", "User has no NetSuite email configured.");
}
```

- [ ] **Step 4: Run, expect pass.**

- [ ] **Step 5: Commit**

```bash
git add src/Auth.Domain/Users/ tests/Auth.Domain.UnitTests/Users/
git commit -m "feat(auth): add User entity with JIT and pre-provision states"
```

### Task 1.3: `Permission` + `Role` + `Group` entities

**Files:**
- Create: `src/Auth.Domain/Permissions/Permission.cs`, `PermissionCodes.cs`
- Create: `src/Auth.Domain/Roles/Role.cs`, `RoleErrors.cs`
- Create: `src/Auth.Domain/Groups/Group.cs`, `GroupErrors.cs`
- Create: `tests/Auth.Domain.UnitTests/Permissions/PermissionTests.cs`
- Create: `tests/Auth.Domain.UnitTests/Roles/RoleTests.cs`
- Create: `tests/Auth.Domain.UnitTests/Groups/GroupTests.cs`

- [ ] **Step 1: Write failing tests for all three**

```csharp
// PermissionTests.cs
using Auth.Domain.Permissions;
using Shouldly;
namespace Auth.Domain.UnitTests.Permissions;
public sealed class PermissionTests
{
    [Fact]
    public void Create_ShouldStoreCodeAndDescription()
    {
        var p = Permission.Create("users.read", "Read users");
        p.Code.ShouldBe("users.read");
        p.Description.ShouldBe("Read users");
    }
}
```

```csharp
// RoleTests.cs
using Auth.Domain.Permissions;
using Auth.Domain.Roles;
using Shouldly;
namespace Auth.Domain.UnitTests.Roles;
public sealed class RoleTests
{
    [Fact]
    public void AssignPermission_AddsOnce()
    {
        var role = Role.Create(Guid.NewGuid(), "admin", "Admins");
        var p = Permission.Create("users.read", "");
        role.AssignPermission(p.Id);
        role.AssignPermission(p.Id); // idempotent
        role.PermissionIds.Count.ShouldBe(1);
    }

    [Fact]
    public void RevokePermission_Removes()
    {
        var role = Role.Create(Guid.NewGuid(), "admin", "");
        var pid = Guid.NewGuid();
        role.AssignPermission(pid);
        role.RevokePermission(pid);
        role.PermissionIds.ShouldBeEmpty();
    }
}
```

```csharp
// GroupTests.cs
using Auth.Domain.Groups;
using Shouldly;
namespace Auth.Domain.UnitTests.Groups;
public sealed class GroupTests
{
    [Fact]
    public void LinkEntraGroup_StoresEntraGroupId()
    {
        var group = Group.Create(Guid.NewGuid(), "Engineers", "Eng");
        var entraGroupId = Guid.NewGuid();
        group.LinkEntraGroup(entraGroupId);
        group.EntraGroupId.ShouldBe(entraGroupId);
    }
}
```

- [ ] **Step 2: Run, expect failure.**

- [ ] **Step 3: Implement entities**

```csharp
// Permission.cs
using SharedKernel;
namespace Auth.Domain.Permissions;

public sealed class Permission : Entity
{
    private Permission() { }
    public string Code { get; private set; } = null!;
    public string Description { get; private set; } = null!;

    public static Permission Create(string code, string description) =>
        new() { Id = Guid.NewGuid(), Code = code, Description = description, CreatedAt = DateTimeOffset.UtcNow };
}
```

```csharp
// PermissionCodes.cs
namespace Auth.Domain.Permissions;

public static class PermissionCodes
{
    public const string UsersRead = "users.read";
    public const string UsersWrite = "users.write";
    public const string GroupsRead = "groups.read";
    public const string GroupsWrite = "groups.write";
    public const string RolesRead = "roles.read";
    public const string RolesWrite = "roles.write";
    public const string M2MClientsRead = "m2mclients.read";
    public const string M2MClientsWrite = "m2mclients.write";
    public const string AuditRead = "audit.read";

    public static IReadOnlyCollection<(string Code, string Description)> All { get; } =
    [
        (UsersRead, "Read users"),
        (UsersWrite, "Create, update, deactivate users"),
        (GroupsRead, "Read groups"),
        (GroupsWrite, "Manage groups"),
        (RolesRead, "Read roles"),
        (RolesWrite, "Manage roles"),
        (M2MClientsRead, "Read M2M clients"),
        (M2MClientsWrite, "Manage M2M clients"),
        (AuditRead, "Read audit log")
    ];
}
```

```csharp
// Role.cs
using SharedKernel;
namespace Auth.Domain.Roles;

public sealed class Role : Entity
{
    private readonly HashSet<Guid> _permissionIds = [];
    private Role() { }

    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = null!;
    public string Description { get; private set; } = null!;
    public IReadOnlyCollection<Guid> PermissionIds => _permissionIds;

    public static Role Create(Guid tenantId, string name, string description) =>
        new() { Id = Guid.NewGuid(), TenantId = tenantId, Name = name, Description = description, CreatedAt = DateTimeOffset.UtcNow };

    public void AssignPermission(Guid permissionId) => _permissionIds.Add(permissionId);
    public void RevokePermission(Guid permissionId) => _permissionIds.Remove(permissionId);
}
```

```csharp
// RoleErrors.cs
using SharedKernel;
namespace Auth.Domain.Roles;
public static class RoleErrors
{
    public static Error NotFound(Guid id) => Error.NotFound("Role.NotFound", $"Role '{id}' was not found.");
    public static readonly Error NameAlreadyTaken =
        Error.Conflict("Role.NameAlreadyTaken", "A role with this name already exists in this tenant.");
}
```

```csharp
// Group.cs
using SharedKernel;
namespace Auth.Domain.Groups;

public sealed class Group : Entity
{
    private readonly HashSet<Guid> _roleIds = [];
    private Group() { }

    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = null!;
    public string Description { get; private set; } = null!;
    public Guid? EntraGroupId { get; private set; }
    public IReadOnlyCollection<Guid> RoleIds => _roleIds;

    public static Group Create(Guid tenantId, string name, string description) =>
        new() { Id = Guid.NewGuid(), TenantId = tenantId, Name = name, Description = description, CreatedAt = DateTimeOffset.UtcNow };

    public void LinkEntraGroup(Guid entraGroupId) => EntraGroupId = entraGroupId;
    public void UnlinkEntraGroup() => EntraGroupId = null;
    public void AssignRole(Guid roleId) => _roleIds.Add(roleId);
    public void RevokeRole(Guid roleId) => _roleIds.Remove(roleId);
}
```

```csharp
// GroupErrors.cs
using SharedKernel;
namespace Auth.Domain.Groups;
public static class GroupErrors
{
    public static Error NotFound(Guid id) => Error.NotFound("Group.NotFound", $"Group '{id}' was not found.");
    public static readonly Error NameAlreadyTaken =
        Error.Conflict("Group.NameAlreadyTaken", "A group with this name already exists in this tenant.");
}
```

- [ ] **Step 4: Run, expect pass.**

- [ ] **Step 5: Commit**

```bash
git add src/Auth.Domain/{Permissions,Roles,Groups}/ tests/Auth.Domain.UnitTests/{Permissions,Roles,Groups}/
git commit -m "feat(auth): add Permission, Role, Group entities"
```

### Task 1.4: `M2MClient` and `AuthAuditEvent`

**Files:**
- Create: `src/Auth.Domain/M2MClients/M2MClient.cs`, `M2MClientErrors.cs`
- Create: `src/Auth.Domain/Audit/AuthAuditEvent.cs`, `AuthAuditEventType.cs`
- Create: `tests/Auth.Domain.UnitTests/M2MClients/M2MClientTests.cs`
- Create: `tests/Auth.Domain.UnitTests/Audit/AuthAuditEventTests.cs`

- [ ] **Step 1: Tests**

```csharp
// M2MClientTests.cs
using Auth.Domain.M2MClients;
using Shouldly;
namespace Auth.Domain.UnitTests.M2MClients;

public sealed class M2MClientTests
{
    [Fact]
    public void Register_ShouldCreateActiveClient()
    {
        var c = M2MClient.Register(
            tenantId: Guid.NewGuid(),
            clientId: "worker-svc",
            clientSecretHash: "hash",
            displayName: "Worker",
            allowedScopes: ["api:web"]);

        c.ClientId.ShouldBe("worker-svc");
        c.IsActive.ShouldBeTrue();
        c.AllowedScopes.ShouldContain("api:web");
    }

    [Fact]
    public void Deactivate_FlipsIsActive()
    {
        var c = M2MClient.Register(Guid.NewGuid(), "x", "h", "X", ["s"]);
        c.Deactivate();
        c.IsActive.ShouldBeFalse();
    }
}
```

```csharp
// AuthAuditEventTests.cs
using Auth.Domain.Audit;
using Shouldly;
namespace Auth.Domain.UnitTests.Audit;

public sealed class AuthAuditEventTests
{
    [Fact]
    public void Record_ShouldCaptureTenantUserAndIp()
    {
        var ev = AuthAuditEvent.Record(
            tenantId: Guid.NewGuid(),
            userId: Guid.NewGuid(),
            eventType: AuthAuditEventType.LoginSucceeded,
            ip: "10.0.0.1",
            userAgent: "ua",
            detail: "{}");

        ev.EventType.ShouldBe(AuthAuditEventType.LoginSucceeded);
        ev.Ip.ShouldBe("10.0.0.1");
    }
}
```

- [ ] **Step 2: Implement entities**

```csharp
// M2MClient.cs
using SharedKernel;
namespace Auth.Domain.M2MClients;

public sealed class M2MClient : Entity
{
    private readonly List<string> _allowedScopes = [];
    private M2MClient() { }

    public Guid TenantId { get; private set; }
    public string ClientId { get; private set; } = null!;
    public string ClientSecretHash { get; private set; } = null!;
    public string DisplayName { get; private set; } = null!;
    public IReadOnlyCollection<string> AllowedScopes => _allowedScopes;
    public bool IsActive { get; private set; }

    public static M2MClient Register(Guid tenantId, string clientId, string clientSecretHash, string displayName, IEnumerable<string> allowedScopes)
    {
        var client = new M2MClient
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ClientId = clientId,
            ClientSecretHash = clientSecretHash,
            DisplayName = displayName,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        client._allowedScopes.AddRange(allowedScopes);
        return client;
    }

    public void Deactivate() => IsActive = false;
    public void RotateSecret(string newHash) => ClientSecretHash = newHash;
}
```

```csharp
// M2MClientErrors.cs
using SharedKernel;
namespace Auth.Domain.M2MClients;
public static class M2MClientErrors
{
    public static readonly Error NotFound =
        Error.NotFound("M2MClient.NotFound", "Client not found.");
    public static readonly Error InvalidSecret =
        Error.Forbidden("M2MClient.InvalidSecret", "Invalid client credentials.");
    public static readonly Error Inactive =
        Error.Forbidden("M2MClient.Inactive", "Client is inactive.");
}
```

```csharp
// AuthAuditEventType.cs
namespace Auth.Domain.Audit;
public enum AuthAuditEventType
{
    LoginSucceeded,
    LoginFailed,
    UserProvisioned,
    UserDisabled,
    UserEnabled,
    RoleAssigned,
    RoleRevoked,
    GroupCreated,
    M2MTokenIssued,
    TokenRevoked
}
```

```csharp
// AuthAuditEvent.cs
using SharedKernel;
namespace Auth.Domain.Audit;

public sealed class AuthAuditEvent : Entity
{
    private AuthAuditEvent() { }
    public Guid TenantId { get; private set; }
    public Guid? UserId { get; private set; }
    public AuthAuditEventType EventType { get; private set; }
    public string Ip { get; private set; } = null!;
    public string UserAgent { get; private set; } = null!;
    public string Detail { get; private set; } = null!;
    public DateTimeOffset OccurredAt { get; private set; }

    public static AuthAuditEvent Record(Guid tenantId, Guid? userId, AuthAuditEventType eventType, string ip, string userAgent, string detail) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = userId,
            EventType = eventType,
            Ip = ip,
            UserAgent = userAgent,
            Detail = detail,
            OccurredAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow
        };
}
```

- [ ] **Step 3: Run, expect pass. Commit.**

```bash
git add src/Auth.Domain/{M2MClients,Audit}/ tests/Auth.Domain.UnitTests/{M2MClients,Audit}/
git commit -m "feat(auth): add M2MClient and AuthAuditEvent entities"
```

---

## Phase 2 — Application abstractions and handlers (TDD)

### Task 2.1: Application abstractions

**Files:**
- Create: `src/Auth.Application/Abstractions/Data/IAuthDbContext.cs`
- Create: `src/Auth.Application/Abstractions/Tenancy/ITenantContext.cs`
- Create: `src/Auth.Application/Abstractions/Identity/IPermissionResolver.cs`
- Create: `src/Auth.Application/Abstractions/Crypto/IClientSecretHasher.cs`
- Create: `src/Auth.Application/Abstractions/Messaging/ICommand.cs` etc. — copy from existing `src/Application/Abstractions/Messaging/` (same shape, new namespace `Auth.Application.Abstractions.Messaging`).

- [ ] **Step 1: Copy messaging abstractions** (commands/queries/handlers/IMessagePublisher omitted; we don't publish messages from Auth core in V1)

`src/Auth.Application/Abstractions/Messaging/ICommand.cs`:

```csharp
using SharedKernel;
namespace Auth.Application.Abstractions.Messaging;
public interface ICommand : IRequest<Result>;
public interface ICommand<TResponse> : IRequest<Result<TResponse>>;
public interface IRequest<TResponse>;

public interface ICommandHandler<TCommand>
    where TCommand : ICommand
{
    Task<Result> Handle(TCommand command, CancellationToken cancellationToken);
}

public interface ICommandHandler<TCommand, TResponse>
    where TCommand : ICommand<TResponse>
{
    Task<Result<TResponse>> Handle(TCommand command, CancellationToken cancellationToken);
}
```

`IQuery.cs` / `IQueryHandler.cs`: identical shape to existing scaffold.

- [ ] **Step 2: `IAuthDbContext`**

```csharp
using Auth.Domain.Audit;
using Auth.Domain.Groups;
using Auth.Domain.M2MClients;
using Auth.Domain.Permissions;
using Auth.Domain.Roles;
using Auth.Domain.Tenants;
using Auth.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Auth.Application.Abstractions.Data;

public interface IAuthDbContext
{
    DbSet<Tenant> Tenants { get; }
    DbSet<User> Users { get; }
    DbSet<Group> Groups { get; }
    DbSet<Role> Roles { get; }
    DbSet<Permission> Permissions { get; }
    DbSet<M2MClient> M2MClients { get; }
    DbSet<AuthAuditEvent> AuditEvents { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
```

`Auth.Application.csproj` needs `<PackageReference Include="Microsoft.EntityFrameworkCore" />` to expose `DbSet`. Add it.

- [ ] **Step 3: `ITenantContext`**

```csharp
namespace Auth.Application.Abstractions.Tenancy;

public interface ITenantContext
{
    /// <summary>Tenant id from the current authenticated principal. Throws if no tenant is in scope.</summary>
    Guid TenantId { get; }

    /// <summary>True when there is a tenant in scope (e.g. inside a request); false during startup or background work.</summary>
    bool HasTenant { get; }
}
```

- [ ] **Step 4: `IPermissionResolver`**

```csharp
namespace Auth.Application.Abstractions.Identity;

public interface IPermissionResolver
{
    Task<IReadOnlyCollection<string>> ResolveAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken);
}
```

- [ ] **Step 5: `IClientSecretHasher`**

```csharp
namespace Auth.Application.Abstractions.Crypto;

public interface IClientSecretHasher
{
    string Hash(string secret);
    bool Verify(string secret, string hash);
}
```

- [ ] **Step 6: Build, commit**

```bash
dotnet build src/Auth.Application/Auth.Application.csproj
git add src/Auth.Application/Abstractions/ Directory.Packages.props
git commit -m "feat(auth): add Application abstractions (data, tenancy, identity, crypto)"
```

### Task 2.2: `ResolveTenantQuery` (Entra `tid` → local Tenant)

**Files:**
- Create: `tests/Auth.Application.UnitTests/Tenants/Resolve/ResolveTenantQueryHandlerTests.cs`
- Create: `src/Auth.Application/Tenants/Resolve/ResolveTenantQuery.cs`
- Create: `src/Auth.Application/Tenants/Resolve/ResolveTenantQueryHandler.cs`

- [ ] **Step 1: Failing test**

```csharp
using Auth.Application.Abstractions.Data;
using Auth.Application.Tenants.Resolve;
using Auth.Domain.Tenants;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Auth.Application.UnitTests.Tenants.Resolve;

public sealed class ResolveTenantQueryHandlerTests
{
    [Fact]
    public async Task Should_ReturnTenant_WhenEntraTenantIdMatches()
    {
        await using var db = CreateDb();
        var entraTid = Guid.NewGuid();
        var t = Tenant.Create(entraTid, "Acme", "https://acme");
        db.Tenants.Add(t);
        await db.SaveChangesAsync();

        var handler = new ResolveTenantQueryHandler(db);
        var result = await handler.Handle(new ResolveTenantQuery(entraTid), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Id.ShouldBe(t.Id);
    }

    [Fact]
    public async Task Should_FailNotRegistered_WhenNoMatch()
    {
        await using var db = CreateDb();
        var handler = new ResolveTenantQueryHandler(db);
        var result = await handler.Handle(new ResolveTenantQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Tenant.NotRegistered");
    }

    [Fact]
    public async Task Should_FailInactive_WhenTenantIsDeactivated()
    {
        await using var db = CreateDb();
        var entraTid = Guid.NewGuid();
        var t = Tenant.Create(entraTid, "X", "https://x");
        t.Deactivate();
        db.Tenants.Add(t);
        await db.SaveChangesAsync();

        var handler = new ResolveTenantQueryHandler(db);
        var result = await handler.Handle(new ResolveTenantQuery(entraTid), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Tenant.Inactive");
    }

    private static TestAuthDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<TestAuthDbContext>()
            .UseInMemoryDatabase($"auth-{Guid.NewGuid()}").Options);
}
```

Add a small `TestAuthDbContext.cs` in the test project that implements `IAuthDbContext` over EF InMemory. Pattern is identical to existing scaffold's `GetSampleEntityByIdQueryHandlerTests` — see `tests/Application.UnitTests/SampleEntities/` for the template.

- [ ] **Step 2: Run, expect compile failures.**

- [ ] **Step 3: Implement query + handler**

```csharp
// ResolveTenantQuery.cs
using Auth.Application.Abstractions.Messaging;
using Auth.Domain.Tenants;
namespace Auth.Application.Tenants.Resolve;
public sealed record ResolveTenantQuery(Guid EntraTenantId) : IQuery<Tenant>;
```

```csharp
// ResolveTenantQueryHandler.cs
using Auth.Application.Abstractions.Data;
using Auth.Application.Abstractions.Messaging;
using Auth.Domain.Tenants;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Auth.Application.Tenants.Resolve;

public sealed class ResolveTenantQueryHandler(IAuthDbContext db)
    : IQueryHandler<ResolveTenantQuery, Tenant>
{
    public async Task<Result<Tenant>> Handle(ResolveTenantQuery query, CancellationToken cancellationToken)
    {
        var tenant = await db.Tenants
            .FirstOrDefaultAsync(t => t.EntraTenantId == query.EntraTenantId, cancellationToken);

        if (tenant is null)
            return Result.Failure<Tenant>(TenantErrors.NotRegistered(query.EntraTenantId));
        if (!tenant.IsActive)
            return Result.Failure<Tenant>(TenantErrors.Inactive);
        return tenant;
    }
}
```

- [ ] **Step 4: Run tests, expect pass. Commit.**

```bash
git add src/Auth.Application/Tenants/ tests/Auth.Application.UnitTests/Tenants/
git commit -m "feat(auth): resolve Entra tenant to local Tenant"
```

### Task 2.3: `SyncEntraUserCommand` (JIT provisioning)

**Files:**
- Create: `src/Auth.Application/Users/SyncEntra/SyncEntraUserCommand.cs`
- Create: `src/Auth.Application/Users/SyncEntra/SyncEntraUserCommandValidator.cs`
- Create: `src/Auth.Application/Users/SyncEntra/SyncEntraUserCommandHandler.cs`
- Create: `tests/Auth.Application.UnitTests/Users/SyncEntra/SyncEntraUserCommandHandlerTests.cs`
- Create: `tests/Auth.Application.UnitTests/Users/SyncEntra/SyncEntraUserCommandValidatorTests.cs`

**Behavior:**
- If user exists by `(TenantId, EntraOid)` → update display name + record login → return user.
- Else if user exists by `(TenantId, Email)` and is pre-provisioned → activate from Entra → return user.
- Else if user exists by `(TenantId, Email)` and `IsActive=false` and not pre-provisioned → return `UserErrors.Disabled`.
- Else → JIT-provision new user → return user.

- [ ] **Step 1: Validator + tests** (Email non-empty, max 320; DisplayName non-empty, max 200; EntraOid not empty; TenantId not empty.)

```csharp
// Command
using Auth.Application.Abstractions.Messaging;
namespace Auth.Application.Users.SyncEntra;
public sealed record SyncEntraUserCommand(Guid TenantId, Guid EntraOid, string Email, string DisplayName)
    : ICommand<Guid>;
```

```csharp
// Validator
using FluentValidation;
namespace Auth.Application.Users.SyncEntra;
internal sealed class SyncEntraUserCommandValidator : AbstractValidator<SyncEntraUserCommand>
{
    public SyncEntraUserCommandValidator()
    {
        RuleFor(c => c.TenantId).NotEmpty();
        RuleFor(c => c.EntraOid).NotEmpty();
        RuleFor(c => c.Email).NotEmpty().MaximumLength(320).EmailAddress();
        RuleFor(c => c.DisplayName).NotEmpty().MaximumLength(200);
    }
}
```

- [ ] **Step 2: Handler tests** — four scenarios above. Use `TestAuthDbContext` over EF InMemory.

- [ ] **Step 3: Implement handler**

```csharp
using Auth.Application.Abstractions.Data;
using Auth.Application.Abstractions.Messaging;
using Auth.Domain.Users;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Auth.Application.Users.SyncEntra;

public sealed class SyncEntraUserCommandHandler(IAuthDbContext db)
    : ICommandHandler<SyncEntraUserCommand, Guid>
{
    public async Task<Result<Guid>> Handle(SyncEntraUserCommand cmd, CancellationToken ct)
    {
        var byOid = await db.Users
            .FirstOrDefaultAsync(u => u.TenantId == cmd.TenantId && u.EntraOid == cmd.EntraOid, ct);
        if (byOid is not null)
        {
            if (!byOid.IsActive) return Result.Failure<Guid>(UserErrors.Disabled);
            byOid.RecordLogin();
            await db.SaveChangesAsync(ct);
            return byOid.Id;
        }

        var byEmail = await db.Users
            .FirstOrDefaultAsync(u => u.TenantId == cmd.TenantId && u.Email == cmd.Email, ct);
        if (byEmail is not null)
        {
            if (byEmail.IsPreProvisioned)
            {
                byEmail.ActivateFromEntra(cmd.EntraOid, cmd.DisplayName);
                byEmail.RecordLogin();
                await db.SaveChangesAsync(ct);
                return byEmail.Id;
            }
            return Result.Failure<Guid>(UserErrors.Disabled);
        }

        var u = User.ProvisionFromEntra(cmd.TenantId, cmd.EntraOid, cmd.Email, cmd.DisplayName);
        u.RecordLogin();
        db.Users.Add(u);
        await db.SaveChangesAsync(ct);
        return u.Id;
    }
}
```

- [ ] **Step 4: Run tests, pass. Commit.**

```bash
git add src/Auth.Application/Users/SyncEntra/ tests/Auth.Application.UnitTests/Users/SyncEntra/
git commit -m "feat(auth): JIT user provisioning from Entra claims"
```

### Task 2.4: `GetEffectivePermissionsQuery`

**Files:**
- Create: `src/Auth.Application/Users/GetEffectivePermissions/GetEffectivePermissionsQuery.cs` (+Handler)
- Create: `tests/Auth.Application.UnitTests/Users/GetEffectivePermissions/GetEffectivePermissionsQueryHandlerTests.cs`

**Algorithm:** union of permissions from `UserRoles` and from roles attached to user's `Groups`.

- [ ] **Step 1: Test scenarios** — direct role only; group role only; both with overlap (deduped); inactive user returns empty; user not found returns failure.

- [ ] **Step 2: Implement** — compose query with EF; return `IReadOnlyCollection<string>` of permission codes.

Skeleton:

```csharp
public sealed record GetEffectivePermissionsQuery(Guid TenantId, Guid UserId) : IQuery<IReadOnlyCollection<string>>;

public sealed class GetEffectivePermissionsQueryHandler(IAuthDbContext db)
    : IQueryHandler<GetEffectivePermissionsQuery, IReadOnlyCollection<string>>
{
    public async Task<Result<IReadOnlyCollection<string>>> Handle(GetEffectivePermissionsQuery q, CancellationToken ct)
    {
        var userExists = await db.Users.AnyAsync(u => u.Id == q.UserId && u.TenantId == q.TenantId && u.IsActive, ct);
        if (!userExists) return Result.Failure<IReadOnlyCollection<string>>(UserErrors.NotFound(q.UserId));

        // direct role permission ids
        var directPermissionIds = await db.Roles
            .Where(r => r.TenantId == q.TenantId)
            .Where(r => db.Set<UserRoleJoin>().Any(j => j.UserId == q.UserId && j.RoleId == r.Id))
            .SelectMany(r => r.PermissionIds)
            .ToListAsync(ct);

        // group role permission ids — similar shape via UserGroups + GroupRoles
        // (left as a single LINQ chain; see implementation details in PR)

        var allIds = directPermissionIds /* .Union(groupPermissionIds) */.Distinct();

        var codes = await db.Permissions
            .Where(p => allIds.Contains(p.Id))
            .Select(p => p.Code)
            .ToListAsync(ct);

        return codes;
    }
}
```

> ⚠️ The skeleton above relies on join entities (`UserRoleJoin`, `UserGroupJoin`, `GroupRoleJoin`). EF mapping for these joins is configured in Phase 3 (Task 3.1). Until then, write the handler to use `db.Set<UserRoleJoin>()` etc. and expect the configuration step to register the join entities. The unit tests use `TestAuthDbContext` which mirrors the configurations.

- [ ] **Step 3: Run, pass. Commit.**

```bash
git add src/Auth.Application/Users/GetEffectivePermissions/ tests/Auth.Application.UnitTests/Users/GetEffectivePermissions/
git commit -m "feat(auth): resolve effective permissions for a user"
```

### Task 2.5: `AuthenticateM2MClientCommand`

**Files:**
- Create: `src/Auth.Application/M2MClients/Authenticate/AuthenticateM2MClientCommand.cs` (+Handler)
- Create: `tests/Auth.Application.UnitTests/M2MClients/Authenticate/...`

- [ ] **Step 1: Tests** — valid client+secret returns clientId+tenantId; invalid secret returns `InvalidSecret`; inactive returns `Inactive`; missing returns `NotFound`.

- [ ] **Step 2: Implement**

```csharp
public sealed record AuthenticateM2MClientCommand(string ClientId, string ClientSecret) : ICommand<M2MClientAuthenticated>;
public sealed record M2MClientAuthenticated(Guid TenantId, string ClientId, IReadOnlyCollection<string> AllowedScopes);

public sealed class AuthenticateM2MClientCommandHandler(IAuthDbContext db, IClientSecretHasher hasher)
    : ICommandHandler<AuthenticateM2MClientCommand, M2MClientAuthenticated>
{
    public async Task<Result<M2MClientAuthenticated>> Handle(AuthenticateM2MClientCommand cmd, CancellationToken ct)
    {
        var client = await db.M2MClients.FirstOrDefaultAsync(c => c.ClientId == cmd.ClientId, ct);
        if (client is null) return Result.Failure<M2MClientAuthenticated>(M2MClientErrors.NotFound);
        if (!client.IsActive) return Result.Failure<M2MClientAuthenticated>(M2MClientErrors.Inactive);
        if (!hasher.Verify(cmd.ClientSecret, client.ClientSecretHash))
            return Result.Failure<M2MClientAuthenticated>(M2MClientErrors.InvalidSecret);

        return new M2MClientAuthenticated(client.TenantId, client.ClientId, [.. client.AllowedScopes]);
    }
}
```

- [ ] **Step 3: Commit.**

```bash
git add src/Auth.Application/M2MClients/ tests/Auth.Application.UnitTests/M2MClients/
git commit -m "feat(auth): authenticate M2M client for client_credentials grant"
```

### Task 2.6: `Auth.Application.DependencyInjection`

**Files:**
- Create: `src/Auth.Application/DependencyInjection.cs`

Mirror existing scaffold's `Application.DependencyInjection`:

```csharp
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Scrutor;

namespace Auth.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddAuthApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssemblyContaining<DependencyInjectionMarker>(includeInternalTypes: true);

        services.Scan(scan => scan
            .FromAssemblyOf<DependencyInjectionMarker>()
            .AddClasses(c => c.AssignableTo(typeof(ICommandHandler<>))).AsImplementedInterfaces().WithScopedLifetime()
            .AddClasses(c => c.AssignableTo(typeof(ICommandHandler<,>))).AsImplementedInterfaces().WithScopedLifetime()
            .AddClasses(c => c.AssignableTo(typeof(IQueryHandler<,>))).AsImplementedInterfaces().WithScopedLifetime());

        // Validation decorators (copy ValidationDecorator implementation from existing Application project — same pattern)
        services.TryDecorate(typeof(ICommandHandler<,>), typeof(ValidationDecorator.CommandHandler<,>));
        services.TryDecorate(typeof(ICommandHandler<>),  typeof(ValidationDecorator.CommandBaseHandler<>));

        return services;
    }
}

internal sealed class DependencyInjectionMarker;
```

- [ ] **Step 1: Copy `ValidationDecorator` from `src/Application/Abstractions/Behaviors/ValidationDecorator.cs` into `src/Auth.Application/Abstractions/Behaviors/ValidationDecorator.cs` adjusting namespace.**

- [ ] **Step 2: Run all Auth.Application unit tests** — green.

- [ ] **Step 3: Commit.**

```bash
git add src/Auth.Application/DependencyInjection.cs src/Auth.Application/Abstractions/Behaviors/
git commit -m "feat(auth): wire Application DI with validation decorator"
```

---

## Phase 3 — Infrastructure: DbContext, configurations, migration

### Task 3.1: `AuthDbContext` and EF configurations

**Files:**
- Create: `src/Auth.Infra/Database/Schemas.cs` (`public const string Auth = "auth";`)
- Create: `src/Auth.Infra/Database/AuthDbContext.cs`
- Create: `src/Auth.Infra/Database/JoinEntities.cs` (UserRoleJoin, UserGroupJoin, GroupRoleJoin, RolePermissionJoin)
- Create: `src/Auth.Infra/Config/*.cs` for each entity (use `AbstractConfiguration<T>` pattern from existing `Infra/Config/`)

- [ ] **Step 1: `Schemas.cs`**

```csharp
namespace Auth.Infra.Database;
public static class Schemas { public const string Auth = "auth"; }
```

- [ ] **Step 2: Join entities (explicit many-to-many)**

```csharp
namespace Auth.Infra.Database;
public sealed record UserRoleJoin(Guid UserId, Guid RoleId);
public sealed record UserGroupJoin(Guid UserId, Guid GroupId);
public sealed record GroupRoleJoin(Guid GroupId, Guid RoleId);
public sealed record RolePermissionJoin(Guid RoleId, Guid PermissionId);
```

- [ ] **Step 3: `AuthDbContext`**

```csharp
using Auth.Application.Abstractions.Data;
using Auth.Application.Abstractions.Tenancy;
using Auth.Domain.Audit;
using Auth.Domain.Groups;
using Auth.Domain.M2MClients;
using Auth.Domain.Permissions;
using Auth.Domain.Roles;
using Auth.Domain.Tenants;
using Auth.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Auth.Infra.Database;

public sealed class AuthDbContext(DbContextOptions<AuthDbContext> options, ITenantContext tenant)
    : DbContext(options), IAuthDbContext
{
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Group> Groups => Set<Group>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<M2MClient> M2MClients => Set<M2MClient>();
    public DbSet<AuthAuditEvent> AuditEvents => Set<AuthAuditEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schemas.Auth);
        modelBuilder.UseOpenIddict();
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AuthDbContext).Assembly);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        if (tenant.HasTenant) GuardTenantBoundary(tenant.TenantId);
        return await base.SaveChangesAsync(cancellationToken);
    }

    private void GuardTenantBoundary(Guid expectedTenantId)
    {
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State is EntityState.Added or EntityState.Modified
                && entry.Metadata.FindProperty("TenantId") is not null)
            {
                var current = (Guid)entry.Property("TenantId").CurrentValue!;
                if (current != expectedTenantId)
                    throw new InvalidOperationException(
                        $"Tenant boundary violation: entity {entry.Metadata.Name} has TenantId={current} but current scope is {expectedTenantId}.");
            }
        }
    }
}
```

- [ ] **Step 4: One `*Configuration.cs` per entity.** Pattern (showing `UserConfiguration` only; the others mirror it):

```csharp
using Auth.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Auth.Infra.Config;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.ToTable("users");
        b.HasKey(u => u.Id);
        b.Property(u => u.Email).IsRequired().HasMaxLength(320);
        b.Property(u => u.DisplayName).IsRequired().HasMaxLength(200);
        b.Property(u => u.NetSuiteEmail).HasMaxLength(320);
        b.HasIndex(u => new { u.TenantId, u.Email }).IsUnique();
        b.HasIndex(u => new { u.TenantId, u.EntraOid })
            .IsUnique()
            .HasFilter("\"entra_oid\" IS NOT NULL");
        b.HasQueryFilter(u => !u.IsDeleted);
    }
}
```

Equivalent files: `TenantConfiguration`, `GroupConfiguration` (with `(TenantId, Name)` unique index), `RoleConfiguration`, `PermissionConfiguration` (`Code` unique), `M2MClientConfiguration` (`(ClientId)` unique), `AuthAuditEventConfiguration`. Configure the four join entities (`HasKey(j => new { j.UserId, j.RoleId })` etc.) as part of `OnModelCreating` or via dedicated configuration files.

For the role↔permission and group↔role relationships, configure many-to-many through the explicit join entities so `Role.PermissionIds` materializes via shadow navigation (see EF Core 7+ "Skip navigations to a join entity").

- [ ] **Step 5: Build, but no migration yet**

```bash
dotnet build src/Auth.Infra/Auth.Infra.csproj
```

- [ ] **Step 6: Commit**

```bash
git add src/Auth.Infra/Database/ src/Auth.Infra/Config/
git commit -m "feat(auth): AuthDbContext, EF configurations, tenant save guard"
```

### Task 3.2: Tenant context (HttpContext-backed) + permission resolver + secret hasher

**Files:**
- Create: `src/Auth.Infra/Tenancy/TenantContext.cs`
- Create: `src/Auth.Infra/Identity/PermissionResolver.cs`
- Create: `src/Auth.Infra/Identity/Pbkdf2ClientSecretHasher.cs`

- [ ] **Step 1: `TenantContext`**

```csharp
using System.Security.Claims;
using Auth.Application.Abstractions.Tenancy;
using Microsoft.AspNetCore.Http;

namespace Auth.Infra.Tenancy;

internal sealed class TenantContext(IHttpContextAccessor http) : ITenantContext
{
    public bool HasTenant => TryGetTenantId(out _);

    public Guid TenantId => TryGetTenantId(out var id)
        ? id
        : throw new InvalidOperationException("No tenant_id claim on the current principal.");

    private bool TryGetTenantId(out Guid id)
    {
        id = Guid.Empty;
        var principal = http.HttpContext?.User;
        var claim = principal?.FindFirst("tenant_id")?.Value
                  ?? principal?.FindFirst(ClaimTypes.GroupSid)?.Value;
        return claim is not null && Guid.TryParse(claim, out id);
    }
}
```

- [ ] **Step 2: `PermissionResolver`** — concrete impl of `IPermissionResolver` using `IAuthDbContext`. Same algorithm as Task 2.4's handler skeleton, factored into a reusable service.

- [ ] **Step 3: `Pbkdf2ClientSecretHasher`** — wrap or reuse the existing `Infra.Authentication.PasswordHasher` (PBKDF2). Concrete impl of `IClientSecretHasher`.

- [ ] **Step 4: Build + tests for the hasher**

Add `tests/Auth.Application.UnitTests/Identity/PermissionResolverTests.cs` and run.

- [ ] **Step 5: Commit**

```bash
git add src/Auth.Infra/Tenancy/ src/Auth.Infra/Identity/ tests/Auth.Application.UnitTests/Identity/
git commit -m "feat(auth): tenant context, permission resolver, client secret hasher"
```

### Task 3.3: `Auth.Infra.DependencyInjection`

**Files:**
- Create: `src/Auth.Infra/DependencyInjection.cs`

```csharp
using Auth.Application.Abstractions.Crypto;
using Auth.Application.Abstractions.Data;
using Auth.Application.Abstractions.Identity;
using Auth.Application.Abstractions.Tenancy;
using Auth.Infra.Database;
using Auth.Infra.Identity;
using Auth.Infra.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Auth.Infra;

public static class DependencyInjection
{
    public static IServiceCollection AddAuthInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("AuthDb")
            ?? throw new InvalidOperationException("ConnectionStrings:AuthDb is required.");

        services.AddDbContext<AuthDbContext>(options =>
        {
            options.UseNpgsql(connectionString, npg => npg.MigrationsHistoryTable("__ef_migrations_history", Schemas.Auth));
            options.UseSnakeCaseNamingConvention();
            options.UseOpenIddict();
        });
        services.AddScoped<IAuthDbContext>(sp => sp.GetRequiredService<AuthDbContext>());

        services.AddHttpContextAccessor();
        services.AddScoped<ITenantContext, TenantContext>();
        services.AddScoped<IPermissionResolver, PermissionResolver>();
        services.AddSingleton<IClientSecretHasher, Pbkdf2ClientSecretHasher>();

        return services;
    }
}
```

- [ ] **Step 1: Build + commit.**

```bash
git add src/Auth.Infra/DependencyInjection.cs
git commit -m "feat(auth): wire Infra DI (PostgreSQL, tenancy, hasher)"
```

### Task 3.4: Initial EF migration

**Files:**
- Create (auto-generated): `src/Auth.Infra/Database/Migrations/*.cs`

- [ ] **Step 1: Wire `Auth.API` minimally so EF tools can find the DbContext** — in `Program.cs` add `builder.Services.AddAuthInfrastructure(builder.Configuration);` and set `appsettings.Development.json` `ConnectionStrings:AuthDb` to a working local connection (`Host=localhost;Port=5433;Database=auth_db;Username=postgres;Password=postgres`).

- [ ] **Step 2: Start local PostgreSQL on a fresh port** (we'll add the proper service to compose in Task 9.2; for now use a one-off):

```bash
docker run -d --name auth-postgres-tmp -p 5433:5432 \
  -e POSTGRES_PASSWORD=postgres -e POSTGRES_DB=auth_db postgres:16-alpine
```

- [ ] **Step 3: Generate migration**

```bash
dotnet ef migrations add InitialAuthSchema \
  --project src/Auth.Infra \
  --startup-project src/EntryPoints/Auth.API \
  --output-dir Database/Migrations
```

Expected: migration files generated. Inspect — should include `auth.tenants`, `auth.users`, `auth.groups`, `auth.roles`, `auth.permissions`, `auth.user_roles`, `auth.user_groups`, `auth.group_roles`, `auth.role_permissions`, `auth.m2m_clients`, `auth.auth_audit_events`, plus all OpenIddict tables.

- [ ] **Step 4: Apply and confirm**

```bash
dotnet ef database update \
  --project src/Auth.Infra \
  --startup-project src/EntryPoints/Auth.API
docker exec auth-postgres-tmp psql -U postgres -d auth_db -c '\dt auth.*'
```

Expected: tables listed.

- [ ] **Step 5: Tear down temp container, commit migration**

```bash
docker rm -f auth-postgres-tmp
git add src/Auth.Infra/Database/Migrations/ src/EntryPoints/Auth.API/Program.cs src/EntryPoints/Auth.API/appsettings.Development.json
git commit -m "feat(auth): initial EF migration (auth schema + OpenIddict tables)"
```

### Task 3.5: Permission seed data

**Files:**
- Create: `src/Auth.Infra/Database/PermissionSeed.cs`
- Modify: `src/Auth.Infra/DependencyInjection.cs` (register seeding hosted service or call on startup)

- [ ] **Step 1: Hosted service that seeds `Permissions` from `PermissionCodes.All` on startup, idempotent** (`OrIgnore` via `WHERE NOT EXISTS`).

- [ ] **Step 2: Test** — integration test (Task 8.x) verifies all `PermissionCodes.All` rows exist after startup.

- [ ] **Step 3: Commit.**

```bash
git add src/Auth.Infra/Database/PermissionSeed.cs src/Auth.Infra/DependencyInjection.cs
git commit -m "feat(auth): seed system permissions on startup"
```

---

## Phase 4 — OpenIddict configuration

### Task 4.1: OpenIddict server registration with reference tokens

**Files:**
- Create: `src/Auth.Infra/OpenIddict/OpenIddictExtensions.cs`
- Modify: `src/Auth.Infra/DependencyInjection.cs`

- [ ] **Step 1: Implement extension**

```csharp
using Auth.Infra.Database;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Quartz;

namespace Auth.Infra.OpenIddict;

public static class OpenIddictExtensions
{
    public static IServiceCollection AddAuthOpenIddict(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddQuartz(opt =>
        {
            opt.UseSimpleTypeLoader();
            opt.UseInMemoryStore();
        });
        services.AddQuartzHostedService(opt => opt.WaitForJobsToComplete = true);

        services.AddOpenIddict()
            .AddCore(o =>
            {
                o.UseEntityFrameworkCore().UseDbContext<AuthDbContext>();
                o.UseQuartz();
            })
            .AddServer(o =>
            {
                o.SetAuthorizationEndpointUris("/connect/authorize")
                 .SetTokenEndpointUris("/connect/token")
                 .SetUserInfoEndpointUris("/connect/userinfo")
                 .SetIntrospectionEndpointUris("/connect/introspect")
                 .SetRevocationEndpointUris("/connect/revocation")
                 .SetEndSessionEndpointUris("/connect/logout");

                o.AllowAuthorizationCodeFlow().RequireProofKeyForCodeExchange()
                 .AllowRefreshTokenFlow()
                 .AllowClientCredentialsFlow();

                o.RegisterScopes("openid", "profile", "email", "offline_access", "api:web", "api:auth");

                // Reference tokens: opaque, validated via /connect/introspect
                o.UseReferenceAccessTokens();
                o.UseReferenceRefreshTokens();

                o.SetAccessTokenLifetime(TimeSpan.FromSeconds(
                    configuration.GetValue<int>("OpenIddict:AccessTokenLifetimeSeconds", 900)));
                o.SetRefreshTokenLifetime(TimeSpan.FromSeconds(
                    configuration.GetValue<int>("OpenIddict:RefreshTokenLifetimeSeconds", 1209600)));

                var signingPath = configuration["OpenIddict:SigningCertificatePath"];
                var encryptionPath = configuration["OpenIddict:EncryptionCertificatePath"];
                if (!string.IsNullOrWhiteSpace(signingPath) && !string.IsNullOrWhiteSpace(encryptionPath))
                {
                    o.AddSigningCertificate(LoadCert(signingPath, configuration["OpenIddict:SigningCertificatePassword"]));
                    o.AddEncryptionCertificate(LoadCert(encryptionPath, configuration["OpenIddict:EncryptionCertificatePassword"]));
                }
                else
                {
                    o.AddDevelopmentSigningCertificate();
                    o.AddEncryptionCertificate(LoadDevEncryptionCert());
                }

                o.UseAspNetCore()
                    .EnableAuthorizationEndpointPassthrough()
                    .EnableTokenEndpointPassthrough()
                    .EnableUserInfoEndpointPassthrough()
                    .EnableEndSessionEndpointPassthrough();
            })
            .AddValidation(o =>
            {
                o.UseLocalServer();
                o.UseAspNetCore();
            });

        return services;
    }

    private static System.Security.Cryptography.X509Certificates.X509Certificate2 LoadCert(string path, string? password) =>
        new(path, password, System.Security.Cryptography.X509Certificates.X509KeyStorageFlags.MachineKeySet);

    private static System.Security.Cryptography.X509Certificates.X509Certificate2 LoadDevEncryptionCert()
    {
        // Stable dev cert generation — see OpenIddict samples; do NOT use in production
        return OpenIddict.Server.OpenIddictServerHelpers.CreateEphemeralEncryptionCertificate();
    }
}
```

- [ ] **Step 2: Call `AddAuthOpenIddict` from `AddAuthInfrastructure`** (or expose as a separate `AddAuthIdentityServer` step on `Program.cs`).

- [ ] **Step 3: Build, commit.**

```bash
git add src/Auth.Infra/OpenIddict/ src/Auth.Infra/DependencyInjection.cs
git commit -m "feat(auth): register OpenIddict server with reference tokens + Quartz token cleanup"
```

### Task 4.2: Redis distributed cache + introspection cache wiring

- [ ] **Step 1: Register Redis in `AddAuthInfrastructure`**:

```csharp
var redis = configuration["Redis:ConnectionString"]
    ?? throw new InvalidOperationException("Redis:ConnectionString is required.");
services.AddStackExchangeRedisCache(o => o.Configuration = redis);
services.AddDataProtection().PersistKeysToStackExchangeRedis(StackExchange.Redis.ConnectionMultiplexer.Connect(redis), "auth-dp-keys");
```

(Add `Microsoft.AspNetCore.DataProtection.StackExchangeRedis` to packages.)

- [ ] **Step 2: Smoke build, commit.**

```bash
git add src/Auth.Infra/DependencyInjection.cs Directory.Packages.props
git commit -m "feat(auth): Redis distributed cache + DataProtection key ring"
```

---

## Phase 5 — Auth.API entrypoint, Entra federation, OIDC endpoints

### Task 5.1: Wire `Program.cs` with serilog + OTel + auth + OpenIddict

**Files:**
- Modify: `src/EntryPoints/Auth.API/Program.cs`
- Create: `src/EntryPoints/Auth.API/DependencyInjection.cs`

```csharp
// Program.cs
using Auth.API;
using Auth.Application;
using Auth.Infra;
using Auth.Infra.OpenIddict;
using Infra.Observability;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, sp, cfg) => cfg.ReadFrom.Configuration(ctx.Configuration).ReadFrom.Services(sp));

builder.Services.AddOpenTelemetryObservability(builder.Configuration, serviceName: "Auth.API", includeAspNetCore: true);
builder.Services.AddAuthApplication();
builder.Services.AddAuthInfrastructure(builder.Configuration);
builder.Services.AddAuthOpenIddict(builder.Configuration);
builder.Services.AddAuthApiPresentation(builder.Configuration);

var app = builder.Build();
app.UseSerilogRequestLogging();
app.UseAuthentication();
app.UseAuthorization();
app.MapEndpoints(); // extension scanning IEndpoint
app.MapGet("/health/live", () => Results.Ok(new { status = "live" }));
app.MapGet("/health/ready", () => Results.Ok(new { status = "ready" }));
app.Run();

namespace Auth.API { public partial class Program; }
```

`AddAuthApiPresentation` registers Entra OIDC handler, endpoint discovery, ProblemDetails, Swagger.

- [ ] **Step 1: Implement `AddAuthApiPresentation` and `MapEndpoints` mirroring existing `Web.API.DependencyInjection` and `Web.API.Extensions.EndpointExtensions`.**

- [ ] **Step 2: Smoke run**

```bash
dotnet run --project src/EntryPoints/Auth.API
```

In another terminal:

```bash
curl http://localhost:5000/.well-known/openid-configuration | jq .
```

Expected: discovery document returned. Stop the process.

- [ ] **Step 3: Commit.**

```bash
git add src/EntryPoints/Auth.API/
git commit -m "feat(auth): Auth.API composition root with OIDC discovery"
```

### Task 5.2: Entra OIDC federation handler

**Files:**
- Create: `src/EntryPoints/Auth.API/Authentication/EntraAuthenticationOptions.cs`
- Create: `src/EntryPoints/Auth.API/Authentication/EntraAuthenticationExtensions.cs`

- [ ] **Step 1: Add `Microsoft.AspNetCore.Authentication.OpenIdConnect` and `Microsoft.IdentityModel.Protocols.OpenIdConnect` to `Auth.API.csproj`.**

- [ ] **Step 2: Register the handler**

```csharp
public static IServiceCollection AddEntraExternal(this IServiceCollection services, IConfiguration configuration)
{
    var section = configuration.GetSection("Entra");

    services.AddAuthentication()
        .AddOpenIdConnect("Entra", options =>
        {
            options.Authority = section["Authority"];      // https://login.microsoftonline.com/{tenant}/v2.0  (or "common" for multi-tenant)
            options.ClientId = section["ClientId"];
            options.ClientSecret = section["ClientSecret"];
            options.ResponseType = "code";
            options.UsePkce = true;
            options.SaveTokens = true;
            options.GetClaimsFromUserInfoEndpoint = true;
            options.Scope.Add("openid"); options.Scope.Add("profile"); options.Scope.Add("email");
            options.CallbackPath = "/signin-entra";
            options.TokenValidationParameters.NameClaimType = "name";
            options.TokenValidationParameters.RoleClaimType = "roles";
        });

    return services;
}
```

For multi-tenancy V1, set `Authority = https://login.microsoftonline.com/common/v2.0` and disable issuer validation per Microsoft docs (validate `tid` against our `Tenants` table instead).

- [ ] **Step 3: Hook into `AddAuthApiPresentation`. Build. Commit.**

```bash
git add src/EntryPoints/Auth.API/Authentication/ Directory.Packages.props
git commit -m "feat(auth): wire Entra OIDC federated handler"
```

### Task 5.3: `/connect/authorize` endpoint with Entra challenge + JIT

**Files:**
- Create: `src/EntryPoints/Auth.API/Endpoints/Authorize/AuthorizeEndpoint.cs`

**Behavior:**
1. If user is not authenticated against the `Entra` cookie scheme yet → `Challenge("Entra")` with returnUrl back to authorize.
2. After Entra succeeds, extract `tid`, `oid`, `email`, `name` from `HttpContext.User`.
3. Resolve tenant via `ResolveTenantQuery`. If failure → `Forbid` with reason claim.
4. JIT user via `SyncEntraUserCommand`. If failure → `Forbid`.
5. Call `IPermissionResolver.ResolveAsync(...)` → permission claim list.
6. Build OpenIddict `ClaimsIdentity` with `subject = userId`, `tenant_id = tenantId`, `permission` (multiple), `email`, `name`. Set destinations on each claim.
7. `SignIn(new ClaimsPrincipal(identity), OpenIddictServerAspNetCoreDefaults.AuthenticationScheme)`.

**Skeleton:**

```csharp
internal sealed class AuthorizeEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapMethods("/connect/authorize", new[] { "GET", "POST" }, async (
            HttpContext http,
            IQueryHandler<ResolveTenantQuery, Tenant> resolveTenant,
            ICommandHandler<SyncEntraUserCommand, Guid> syncUser,
            IPermissionResolver permissions,
            CancellationToken ct) =>
        {
            // OpenIddict request lives at http.GetOpenIddictServerRequest()
            var request = http.GetOpenIddictServerRequest()!;

            var entra = await http.AuthenticateAsync("Entra");
            if (!entra.Succeeded)
            {
                return Results.Challenge(new AuthenticationProperties
                {
                    RedirectUri = http.Request.Path + http.Request.QueryString
                }, ["Entra"]);
            }

            var tid = Guid.Parse(entra.Principal!.FindFirstValue("tid")!);
            var oid = Guid.Parse(entra.Principal.FindFirstValue("oid")!);
            var email = entra.Principal.FindFirstValue("preferred_username")
                       ?? entra.Principal.FindFirstValue("email")!;
            var displayName = entra.Principal.FindFirstValue("name") ?? email;

            var tenantR = await resolveTenant.Handle(new ResolveTenantQuery(tid), ct);
            if (tenantR.IsFailure) return Results.Forbid();

            var userR = await syncUser.Handle(
                new SyncEntraUserCommand(tenantR.Value.Id, oid, email, displayName), ct);
            if (userR.IsFailure) return Results.Forbid();

            var perms = await permissions.ResolveAsync(tenantR.Value.Id, userR.Value, ct);

            var identity = new ClaimsIdentity(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                Claims.Name, Claims.Role);
            identity.AddClaim(Claims.Subject, userR.Value.ToString());
            identity.AddClaim("tenant_id", tenantR.Value.Id.ToString());
            identity.AddClaim(Claims.Email, email);
            identity.AddClaim(Claims.Name, displayName);
            foreach (var p in perms) identity.AddClaim("permission", p);

            // Set destinations: token + idToken
            foreach (var claim in identity.Claims)
                claim.SetDestinations([Destinations.AccessToken, Destinations.IdentityToken]);

            return Results.SignIn(new ClaimsPrincipal(identity),
                authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        });
    }
}
```

- [ ] **Step 1: Implement and build.**
- [ ] **Step 2: Manual smoke (Task 8 will automate)** — set Entra config to a working tenant in dev and walk through the flow.
- [ ] **Step 3: Commit.**

```bash
git add src/EntryPoints/Auth.API/Endpoints/Authorize/
git commit -m "feat(auth): /connect/authorize with Entra federation, tenant resolve, JIT, permissions"
```

### Task 5.4: `/connect/token` (auth code + refresh token + client_credentials)

**File:** `src/EntryPoints/Auth.API/Endpoints/Token/TokenEndpoint.cs`

Three branches based on `request.GrantType`:

```csharp
if (request.IsAuthorizationCodeGrantType() || request.IsRefreshTokenGrantType())
{
    var info = await http.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    var principal = info.Principal!;
    // Refresh permissions if grant is refresh — re-call permission resolver to keep token current
    return Results.SignIn(principal, authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
}

if (request.IsClientCredentialsGrantType())
{
    var clientAuth = await m2mAuthenticator.Handle(
        new AuthenticateM2MClientCommand(request.ClientId!, request.ClientSecret!), ct);
    if (clientAuth.IsFailure) return Results.Forbid();

    var identity = new ClaimsIdentity(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    identity.AddClaim(Claims.Subject, request.ClientId!);
    identity.AddClaim("tenant_id", clientAuth.Value.TenantId.ToString());
    foreach (var s in clientAuth.Value.AllowedScopes) identity.AddClaim(Claims.Scope, s);
    foreach (var c in identity.Claims) c.SetDestinations([Destinations.AccessToken]);
    return Results.SignIn(new ClaimsPrincipal(identity), authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
}

return Results.BadRequest(new { error = "unsupported_grant_type" });
```

- [ ] **Step 1: Implement, build, commit.**

```bash
git add src/EntryPoints/Auth.API/Endpoints/Token/
git commit -m "feat(auth): /connect/token (auth code, refresh, client_credentials)"
```

### Task 5.5: `/connect/userinfo`, `/connect/introspect`, `/connect/revocation`

OpenIddict provides default handlers for introspect and revocation when passthrough is enabled — but we want custom behavior on userinfo (return tenant + permissions).

- [ ] **Step 1: `UserInfoEndpoint`** — returns `{ sub, email, name, tenant_id, permission: [...] }` from the current principal.

- [ ] **Step 2: `IntrospectionEndpoint`** — passthrough; OpenIddict handles. Verify by hitting `POST /connect/introspect` with a token + client basic auth (use a seeded resource server client).

- [ ] **Step 3: `RevocationEndpoint`** — passthrough.

- [ ] **Step 4: Build, commit.**

```bash
git add src/EntryPoints/Auth.API/Endpoints/{UserInfo,Introspection,Revocation}/
git commit -m "feat(auth): userinfo/introspect/revocation endpoints"
```

### Task 5.6: Bootstrap OIDC clients on startup (BFF + introspection clients)

**File:** `src/Auth.Infra/Database/OpenIddictClientSeed.cs`

Hosted service that, at startup, ensures the following OpenIddict applications exist:
- `bff-blazor` — confidential client for the Blazor BFF (auth code+PKCE+offline_access).
- `web-api` — resource server with client_id/secret used to call `/connect/introspect`.
- `gateway` — resource server (same as web-api).

Secrets configured via env vars (`OPENIDDICT_BFF_SECRET`, `OPENIDDICT_WEB_API_SECRET`, `OPENIDDICT_GATEWAY_SECRET`). If env vars missing, fail fast in production; in `Development`, use a seeded value documented in `appsettings.Development.json`.

- [ ] **Step 1: Implement seed, register, build, commit.**

```bash
git add src/Auth.Infra/Database/OpenIddictClientSeed.cs
git commit -m "feat(auth): seed OpenIddict applications (bff-blazor, web-api, gateway)"
```

---

## Phase 6 — Architecture tests

### Task 6.1: Auth-specific NetArchTest rules

**File:** `tests/Auth.API.IntegrationTests/Architecture/ArchitectureTests.cs`

Mirror existing `tests/Web.API.IntegrationTests/Architecture/ArchitectureTests.cs`, then add:

```csharp
[Fact]
public void AuthDomain_ShouldNotDependOn_AuthApplication() { /* NetArchTest */ }

[Fact]
public void AuthDomain_ShouldNotDependOn_AuthInfra() { /* NetArchTest */ }

[Fact]
public void AuthApplication_ShouldNotDependOn_AuthInfra() { /* NetArchTest */ }

[Fact]
public void AuthApplication_ShouldNotDependOn_OpenIddictServer() { /* must not import OpenIddict.Server.* */ }

[Fact]
public void AllAuthCommandHandlers_ShouldBe_Sealed() { /* same as scaffold */ }

[Fact]
public void AllAuthEndpoints_ShouldBe_Sealed() { /* same as scaffold */ }

[Fact]
public void M2MClientFactories_LiveOnlyIn_AuthInfraOrAuthApi() { /* asserts M2MClient creation/persistence is in those layers */ }
```

- [ ] **Step 1: Implement, run, commit.**

```bash
dotnet test tests/Auth.API.IntegrationTests --filter "FullyQualifiedName~Architecture"
git add tests/Auth.API.IntegrationTests/Architecture/
git commit -m "test(auth): architecture rules for Auth.* projects"
```

---

## Phase 7 — Integration tests with PostgreSQL Testcontainer + WireMock

### Task 7.1: `AuthWebApplicationFactory`

**File:** `tests/Auth.API.IntegrationTests/Infrastructure/AuthWebApplicationFactory.cs`

- Spawns a `PostgreSqlContainer` and a `RedisContainer` per fixture.
- Spawns a `WireMockServer` impersonating Entra (`/.well-known/openid-configuration`, `/oauth2/v2.0/token`, `/oauth2/v2.0/authorize`, JWKS).
- Sets env vars and connection strings before `Program.Main`.
- Configures the `Entra` OIDC handler `MetadataAddress` to the WireMock URL.
- Replaces test certificates with stable in-memory ones (`OpenIddict.Server.OpenIddictServerHelpers.CreateEphemeralSigningCertificate()`).
- Exposes helpers: `SeedTenantAsync(entraTid, ...)`, `SeedUserAsync(...)`, `IssueEntraIdTokenForUser(userOid, tid, email)`.

This is ~250 lines but all template — see `Testcontainers.PostgreSql` README and `WireMock.Net` examples.

- [ ] **Step 1: Implement, build.** No test yet.

- [ ] **Step 2: Commit.**

```bash
git add tests/Auth.API.IntegrationTests/Infrastructure/
git commit -m "test(auth): WebApplicationFactory with PostgreSQL+Redis testcontainers and WireMock Entra"
```

### Task 7.2: OIDC happy path test (auth code → access token → introspect)

**File:** `tests/Auth.API.IntegrationTests/Endpoints/AuthorizeFlowTests.cs`

- [ ] **Step 1: Test scenario**

```csharp
public sealed class AuthorizeFlowTests : IClassFixture<AuthWebApplicationFactory>
{
    [Fact]
    public async Task Login_ProvisionsUserAndIssuesIntrospectableToken()
    {
        await fixture.SeedTenantAsync(entraTid: KnownTid);
        fixture.WireMockEntra.StubUserLogin(oid: KnownOid, email: "diego@acme", tid: KnownTid);

        var client = fixture.CreateClient();
        // 1. GET /connect/authorize?... with PKCE → expect 302 to Entra
        // 2. Follow redirect through WireMock → callback /signin-entra
        // 3. Server issues code → GET /connect/authorize completes → redirects to client redirect_uri with `code`
        // 4. POST /connect/token with code+verifier → 200, body has access_token (opaque), refresh_token, id_token
        // 5. POST /connect/introspect (basic auth as web-api client) with access_token → active=true, tenant_id, sub, permission
    }
}
```

- [ ] **Step 2: Implement using `HttpClient` with cookie container + manual handling of the Set-Cookie chain.**

- [ ] **Step 3: Run, expect green. Commit.**

```bash
git add tests/Auth.API.IntegrationTests/Endpoints/AuthorizeFlowTests.cs
git commit -m "test(auth): integration test for OIDC code → introspection happy path"
```

### Task 7.3: Failure-path tests

Add separate test methods (each its own `[Fact]`):

- `Login_ReturnsForbidden_WhenTenantNotRegistered`
- `Login_ReturnsForbidden_WhenUserDisabled`
- `Login_ActivatesPreProvisionedUser`
- `Introspect_ReturnsInactive_AfterRevocation`
- `ClientCredentials_IssuesTokenForActiveM2MClient`
- `ClientCredentials_RejectsInvalidSecret`
- `ClientCredentials_RejectsInactiveClient`

- [ ] **Step 1: Implement each.** Each step adds one test, runs it (red → green), commits.

- [ ] **Step 2: After all green, run full suite**

```bash
dotnet test BaseProjectScaffold.sln
```

Expected: original 35+ tests still pass + Auth.* tests all green.

- [ ] **Step 3: Final commit.**

```bash
git add tests/Auth.API.IntegrationTests/Endpoints/
git commit -m "test(auth): integration tests for failure paths and client_credentials"
```

---

## Phase 8 — Observability and ops

### Task 8.1: OTel `ActivitySource` for Auth.API

- [ ] **Step 1: Add `private static readonly ActivitySource AuthSource = new("Auth.API");` in `Program.cs` and register `AddSource("Auth.API")` inside `OpenTelemetryExtensions` (extend the shared method's optional sources param) or call `tracing.AddSource("Auth.API")` explicitly via `services.ConfigureOpenTelemetryTracerProvider(...)`.**

- [ ] **Step 2: Wrap `/connect/authorize` body and `SyncEntraUserCommandHandler.Handle` with `AuthSource.StartActivity(...)`.**

- [ ] **Step 3: Build, commit.**

```bash
git add src/EntryPoints/Auth.API/ src/Auth.Application/Users/SyncEntra/
git commit -m "feat(auth): OTel spans for authorize and JIT provisioning"
```

### Task 8.2: Health checks (db + redis)

- [ ] **Step 1: Add `AddNpgSql` health check + Redis health check to `MapGet("/health/ready")` (or use `AddHealthChecks().AddNpgSql(...).AddRedis(...)`).**

- [ ] **Step 2: Test with `curl http://localhost:5000/health/ready` against running stack.**

- [ ] **Step 3: Commit.**

### Task 8.3: Audit log writes

- [ ] **Step 1: After successful login in `AuthorizeEndpoint`, write `AuthAuditEvent.Record(..., LoginSucceeded, ...)`. After M2M token issuance, write `M2MTokenIssued`. After failure paths, `LoginFailed`.**

- [ ] **Step 2: Integration test: after login, query `auth.auth_audit_events` and assert one row exists.**

- [ ] **Step 3: Commit.**

```bash
git add src/EntryPoints/Auth.API/Endpoints/ tests/Auth.API.IntegrationTests/
git commit -m "feat(auth): audit log on authorize and token endpoints"
```

---

## Phase 9 — Compose, docs, polish

### Task 9.1: `compose.yaml` updates

**File:** `compose.yaml`

- [ ] **Step 1: Add services**

```yaml
auth-postgres:
  image: postgres:16-alpine
  environment:
    POSTGRES_DB: auth_db
    POSTGRES_PASSWORD: postgres
  ports: ["5433:5432"]
  volumes: ["auth-postgres-data:/var/lib/postgresql/data"]

redis:
  image: redis:7-alpine
  ports: ["6379:6379"]

auth-api:
  build:
    context: .
    dockerfile: src/EntryPoints/Auth.API/Dockerfile
  environment:
    ASPNETCORE_ENVIRONMENT: Development
    ConnectionStrings__AuthDb: Host=auth-postgres;Database=auth_db;Username=postgres;Password=postgres
    Redis__ConnectionString: redis:6379
    Entra__Authority: ${ENTRA_AUTHORITY}
    Entra__ClientId: ${ENTRA_CLIENT_ID}
    Entra__ClientSecret: ${ENTRA_CLIENT_SECRET}
    OPENIDDICT_BFF_SECRET: ${OPENIDDICT_BFF_SECRET}
    OPENIDDICT_WEB_API_SECRET: ${OPENIDDICT_WEB_API_SECRET}
    OPENIDDICT_GATEWAY_SECRET: ${OPENIDDICT_GATEWAY_SECRET}
  ports: ["5100:8080"]
  depends_on: [auth-postgres, redis]

volumes:
  auth-postgres-data:
```

- [ ] **Step 2: `Dockerfile` for `Auth.API`** — copy pattern from existing `src/EntryPoints/Web.API/Dockerfile`, change project paths.

- [ ] **Step 3: Smoke**

```bash
docker compose up -d auth-postgres redis
dotnet run --project src/EntryPoints/Auth.API
curl http://localhost:5000/.well-known/openid-configuration
docker compose down
```

- [ ] **Step 4: Commit.**

```bash
git add compose.yaml src/EntryPoints/Auth.API/Dockerfile
git commit -m "chore(auth): compose services and Dockerfile for Auth.API"
```

### Task 9.2: Update `CLAUDE.md` and `README.md`

- [ ] **Step 1: Append to `CLAUDE.md` §3 (directory layout)** the new `Auth.*` projects.
- [ ] **Step 2: Append to `CLAUDE.md` §9 (security)** new env vars: `AUTH_DB_CONNECTION_STRING`, `REDIS_CONNECTION_STRING`, `ENTRA_*`, `OPENIDDICT_*`.
- [ ] **Step 3: Append to `CLAUDE.md` §13 (common pitfalls)**:
  - "OpenIddict reference token returns `active=false` on introspection" → check token isn't past `OPENIDDICT_AccessTokenLifetimeSeconds` and the introspection client is correctly seeded.
  - "Entra `tid` claim missing" → switch authority from `common` to `organizations` or the explicit tenant URL.
- [ ] **Step 4: README — add Auth section.**

- [ ] **Step 5: Commit.**

```bash
git add CLAUDE.md README.md
git commit -m "docs: document Auth.API in CLAUDE.md and README"
```

---

## Final verification

- [ ] **Step 1: Full build + test**

```bash
dotnet build BaseProjectScaffold.sln
dotnet test BaseProjectScaffold.sln
```

Expected: 0 errors, all tests green (existing 35+ plus all Auth.* tests).

- [ ] **Step 2: Lint**

```bash
dotnet format BaseProjectScaffold.sln --verify-no-changes
```

Fix any drift, commit.

- [ ] **Step 3: Smoke end-to-end against compose**

Bring up `auth-postgres + redis + auth-api`, exercise `/connect/authorize` from a curl-driven script using the seeded `bff-blazor` client (fake user via WireMock or via a real Entra dev tenant if available).

- [ ] **Step 4: Tag**

```bash
git tag -a auth-core-v1 -m "Auth.API core complete (Plan 1 of 5)"
```

---

## Self-review (before handoff)

- **Spec coverage:** Every section of `docs/superpowers/specs/2026-05-06-sso-auth-design.md` that falls in scope of "Auth.API core" has tasks: §3 topology (Auth.API role only, gateway/BFF deferred), §4 stack (OpenIddict ✓, ITfoxtec deferred to Plan 4), §5 layout (Auth.Domain/Application/Infra/Auth.API ✓), §6 data model (✓), §7.1 browser login (Auth.API side ✓; BFF side in Plan 3), §7.4 M2M (✓), §7.5 revocation (✓), §8 authorization model (token issuance ✓; downstream policy provider in Plan 5), §10 config env vars (Auth.API subset ✓), §11 conventions (✓), §12 testing (✓), §13 ops (HA notes captured in compose; full HA setup is ops, not code), §14 risks (mitigations in code: tenant guard, signing cert, audit log).
- **Placeholder scan:** No "TBD"; the only deferred details are explicitly cross-referenced to other plans. Skeleton in Task 2.4 marks group-permissions branch with concrete plan to be expanded in PR (acceptable — fully expanded code is ~50 LINQ lines and the algorithm is given).
- **Type consistency:** `Tenant.Create`, `User.ProvisionFromEntra`, `Role.Create(tenantId, name, description)`, `Group.Create(tenantId, name, description)` — names match across tasks. `IPermissionResolver.ResolveAsync(Guid tenantId, Guid userId, CancellationToken)` matches its consumer in `AuthorizeEndpoint`.
- **Spec requirements with no task:** SAML/NetSuite (intentionally deferred to Plan 4), Gateway introspection middleware (Plan 2), Blazor BFF cookie session (Plan 3), Web.API integration (Plan 5).

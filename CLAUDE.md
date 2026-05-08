# CLAUDE.md — AI Agent Instructions

This file is the **single source of truth for AI coding agents** (Claude Code, Cursor, Copilot, Aider, etc.) working on this repository. Read it end-to-end before making changes.

> `AGENTS.md` and `.github/copilot-instructions.md` point here. Update this file — not the pointers.

---

## 1. Project identity

**BaseProjectScaffold** is a .NET 9 Clean Architecture / DDD starter template. It is meant to be **forked** for new backend services. Everything domain-specific is named `SampleEntity` — replace those placeholders when forking.

- Solution file: `BaseProjectScaffold.sln`
- Default branch: `main`
- Target framework: `net10.0` (pinned by `global.json`, SDK 10.0.203+)
- Central Package Management: all versions live in `Directory.Packages.props`
- NuGet packages restore into a repo-local `.nuget-cache/` folder (configured in `nuget.config`, gitignored). This avoids permission issues with the global `~/.nuget/packages/` cache and keeps installs reproducible per checkout. Delete the folder to force a clean restore.
- Entrypoints: `Web.API` (HTTP/JSON), `Web.Blazor` (Blazor Server UI), `Worker` (RabbitMQ consumer), `CronJobs` (Cronos scheduler), `Auth.API` (standalone identity service — separate bounded context with its own DB), `Gateway` (YARP reverse proxy with Redis-backed introspection cache).

---

## 2. Architecture — the non-negotiables

Clean Architecture with strict dependency direction. These rules are **enforced by `tests/Web.API.IntegrationTests/Architecture/ArchitectureTests.cs`** — breaking them breaks the build.

```
Web.API    ──► Infra ──► Application ──► Domain ──► SharedKernel
Web.Blazor ──► Infra
CronJobs   ──► Infra
Worker     ──► Infra
```

**Hard rules:**

| Rule | Why | Enforcement |
|---|---|---|
| `Domain` has **no** dependency on Application / Infra / EF Core | Keep domain pure | NetArchTest |
| `Application` has **no** dependency on Infra / ASP.NET Core | Testable in isolation | NetArchTest |
| `Infra` has **no** dependency on Web.API | Inversion of dependencies | NetArchTest |
| All `ICommandHandler<>` / `ICommandHandler<,>` / `IQueryHandler<,>` implementations must be `sealed` | Perf + clarity | NetArchTest |
| All `IEndpoint` implementations must be `sealed` | Same | NetArchTest |
| Endpoints inject the **handler interface**, never the concrete class | Otherwise the `ValidationDecorator` is bypassed | Manual (see §5) |
| Concrete `IMessagePublisher` implementations must live in `Infra` (not `Application`/`Worker`/`CronJobs`/`Web.API`/`Web.Blazor`) | Multiple entrypoints share a single broker abstraction | NetArchTest |
| OpenTelemetry wiring lives in `Infra.Observability` and is consumed by every entrypoint via `AddOpenTelemetryObservability(...)` | One source of truth for traces/metrics across services | Manual |

**If you need to break one of these rules, stop and justify it in a PR description — don't silently disable the test.**

---

## 3. Directory layout

```
src/
├── SharedKernel/                    # Result, Error, ErrorType, ValidationError, Entity, Enumeration
├── Domain/                          # Aggregates + domain errors (placeholder: SampleEntities/)
├── Application/
│   ├── Abstractions/
│   │   ├── Authentication/          # IUserContext, IPasswordHasher, ITokenProvider, TokenInfo
│   │   ├── Behaviors/               # ValidationDecorator (Scrutor TryDecorate)
│   │   ├── Data/                    # IApplicationDbContext
│   │   └── Messaging/               # ICommand, IQuery, ICommandHandler, IQueryHandler, IMessagePublisher
│   ├── SampleEntities/              # <REPLACE> one folder per feature
│   │   ├── Create/                  # Command + Handler + Validator
│   │   ├── GetById/                 # Query + Handler + Response DTO
│   │   ├── Publish/                 # Command/Validator/Handler that publishes via IMessagePublisher
│   │   └── Events/                  # Message contracts shared across publishers/consumers
│   └── DependencyInjection.cs       # AddApplication() — Scrutor scan + TryDecorate
├── Infra/
│   ├── Authentication/              # UserContext, PasswordHasher, TokenProvider, InvalidClaimException
│   ├── Config/                      # EF entity configurations (AbstractConfiguration<T>)
│   ├── Database/                    # ApplicationDbContext, Schemas
│   ├── Extensions/                  # ConfigurationExtensions (env-var → IConfiguration mapping)
│   ├── Messaging/                   # RabbitMqOptions, RabbitMqConnectionFactory, RabbitMqMessagePublisher
│   ├── Observability/               # OpenTelemetryExtensions (traces + metrics, OTLP exporter)
│   └── DependencyInjection.cs       # AddInfrastructure() + AddInfrastructureMessaging()
└── EntryPoints/
    ├── Web.API/                     # HTTP entrypoint — receives requests, can publish via IMessagePublisher
    │   ├── Endpoints/               # One folder per feature, each file implements IEndpoint
    │   ├── Extensions/              # EndpointExtensions, ResultExtensions, SecurityExtensions,
    │   │                            # ServiceCollectionExtensions
    │   ├── Infrastructure/          # CustomResults (ProblemDetails mapping)
    │   ├── Middleware/              # GlobalExceptionHandlingMiddleware, RequestContextLoggingMiddleware,
    │   │                            # SecurityHeadersMiddleware
    │   ├── Program.cs               # Composition root
    │   ├── DependencyInjection.cs   # AddPresentation()
    │   ├── Dockerfile
    │   ├── appsettings.json         # ⚠️ empty secrets — env vars supply them
    │   └── appsettings.Development.json
    ├── Web.Blazor/                  # BFF (Blazor Server interactive) — cookie session, OIDC client of Auth.API,
    │   │                            # token store in Redis, calls Auth.API admin endpoints via the Gateway.
    │   │                            # No DbContext / EF Core / RabbitMQ dependency — Infra is referenced only
    │   │                            # for OpenTelemetry observability.
    │   ├── Authentication/          # BffAuthenticationExtensions (cookie + OIDC), TokenStore (RedisTokenStore)
    │   ├── Gateway/                 # IAdminGatewayClient (typed HttpClient hitting /api/auth/admin/*)
    │   ├── Components/              # Razor components: layout, PermissionView, admin pages (MudBlazor)
    │   ├── wwwroot/                 # Static assets
    │   ├── Program.cs               # Composition root: Redis + DataProtection + cookie+OIDC + IAdminGatewayClient
    │   ├── Dockerfile
    │   ├── appsettings.json
    │   └── appsettings.Development.json
    ├── CronJobs/                    # BackgroundService + Cronos scheduler — internal polling, publishes events
    │   ├── Jobs/                    # CronBackgroundService, SampleCronJob, SamplePollingJob (publish example)
    │   └── Dockerfile
    ├── Worker/                      # RabbitMQ consumer — extend with one consumer per queue/topic
    │   ├── Messaging/               # SampleMessageConsumer (uses Infra.Messaging primitives)
    │   └── Dockerfile
    ├── Gateway/                     # YARP reverse proxy with introspection cache
    │   ├── Authentication/          # IntrospectionCachingHandler, ForwardedIdentityTransform
    │   ├── HealthChecks/            # AuthApiHealthCheck
    │   ├── Program.cs
    │   ├── DependencyInjection.cs
    │   └── Dockerfile
    └── Auth.API/                    # Standalone identity service — separate bounded context
        ├── Authentication/          # OIDC + OpenIddict wiring
        ├── Endpoints/               # /connect/* endpoints (authorize, token, introspect, userinfo, logout)
        ├── Extensions/              # Service collection + endpoint extensions
        ├── Telemetry/               # ActivitySource + audit logging helpers
        ├── Program.cs               # Composition root (uses Auth.Infra + Auth.Application)
        ├── Dockerfile
        ├── appsettings.json         # ⚠️ empty secrets — env vars supply them
        └── appsettings.Development.json

# Auth bounded context (separate aggregate root + dedicated Postgres + Redis cache)
src/Auth.Domain/                     # Tenants, Users, Roles, Permissions aggregates
src/Auth.Application/                # Auth use cases (sign-in, JIT provisioning, introspection)
src/Auth.Infra/                      # AuthDbContext, OpenIddict EF stores, Redis cache, hosted seeders
                                     #   (OpenIddictClientSeedHostedService, PermissionSeedHostedService)

tests/
├── Domain.UnitTests/                # Pure domain tests
├── Application.UnitTests/
│   ├── Behaviors/                   # ValidationDecoratorTests
│   └── SampleEntities/              # Handler + Validator tests
├── Web.API.IntegrationTests/
│   ├── Architecture/                # NetArchTest rules (covers Auth.* projects too)
│   ├── Endpoints/                   # End-to-end via WebApplicationFactory
│   ├── Infrastructure/              # CustomWebApplicationFactory (EF InMemory + JWT)
│   └── Middleware/                  # GlobalExceptionHandlingMiddleware tests
├── Auth.Domain.UnitTests/           # Auth aggregate tests
├── Auth.Application.UnitTests/      # Auth handler/validator tests
├── Auth.API.IntegrationTests/       # WebApplicationFactory + Postgres/Redis Testcontainers + WireMock Entra
└── Gateway.IntegrationTests/        # YARP introspection cache + forwarded identity tests
```

---

## 4. Essential commands

```bash
# build / test
dotnet restore
dotnet build BaseProjectScaffold.sln
dotnet test BaseProjectScaffold.sln                                    # 35+ tests across 3 suites
dotnet test tests/Application.UnitTests/Application.UnitTests.csproj   # unit only
dotnet test --filter "FullyQualifiedName~<TestName>"                   # single test

# run
dotnet run --project src/EntryPoints/Web.API
dotnet run --project src/EntryPoints/Web.Blazor
dotnet run --project src/EntryPoints/CronJobs
dotnet run --project src/EntryPoints/Worker

# local infra (postgres + rabbitmq + seq)
docker compose up -d
docker compose up -d postgres rabbitmq seq     # infra only, run API from IDE

# EF Core migrations
dotnet ef migrations add <Name> \
  --project src/Infra --startup-project src/EntryPoints/Web.API \
  --output-dir Database/Migrations
dotnet ef database update \
  --project src/Infra --startup-project src/EntryPoints/Web.API

# format / lint (SonarAnalyzer runs during build)
dotnet format BaseProjectScaffold.sln
```

**Before claiming a task done:** `dotnet build` + `dotnet test` must pass cleanly.

---

## 5. How to add a new use case (canonical flow)

Replace `<Feature>` with the aggregate/feature name (e.g. `Orders`) and `<Action>` with the action (e.g. `Create`, `GetById`, `List`, `Update`).

### 5.1 Command / write flow

1. **Command** — `src/Application/<Feature>/<Action>/<Action><Feature>Command.cs`:

   ```csharp
   using Application.Abstractions.Messaging;

   namespace Application.<Feature>.<Action>;

   public sealed record <Action><Feature>Command(string Name, string? Description)
       : ICommand<Guid>;   // or ICommand if no response payload
   ```

2. **Validator** — same folder, `<Action><Feature>CommandValidator.cs`:

   ```csharp
   using FluentValidation;

   namespace Application.<Feature>.<Action>;

   internal sealed class <Action><Feature>CommandValidator
       : AbstractValidator<<Action><Feature>Command>
   {
       public <Action><Feature>CommandValidator()
       {
           RuleFor(c => c.Name).NotEmpty().MaximumLength(200);
       }
   }
   ```

   Validator stays `internal` — `AddValidatorsFromAssembly(..., includeInternalTypes: true)` picks it up.

3. **Handler** — same folder, `<Action><Feature>CommandHandler.cs`:

   ```csharp
   using Application.Abstractions.Data;
   using Application.Abstractions.Messaging;
   using Domain.<Feature>;
   using SharedKernel;

   namespace Application.<Feature>.<Action>;

   public sealed class <Action><Feature>CommandHandler(IApplicationDbContext dbContext)
       : ICommandHandler<<Action><Feature>Command, Guid>
   {
       public async Task<Result<Guid>> Handle(
           <Action><Feature>Command command,
           CancellationToken cancellationToken)
       {
           // no manual validation — ValidationDecorator already ran
           var entity = new <Feature> { Id = Guid.NewGuid(), Name = command.Name, CreatedAt = DateTimeOffset.UtcNow };
           dbContext.<Feature>s.Add(entity);
           await dbContext.SaveChangesAsync(cancellationToken);
           return entity.Id;
       }
   }
   ```

   **Handler must be `public sealed`** (architecture test enforces sealed; public so the endpoint can inject the interface it exposes).

4. **Endpoint** — `src/EntryPoints/Web.API/Endpoints/<Feature>/<Action><Feature>Endpoint.cs`:

   ```csharp
   using Application.Abstractions.Messaging;
   using Application.<Feature>.<Action>;
   using Web.API.Extensions;
   using Web.API.Infrastructure;

   namespace Web.API.Endpoints.<Feature>;

   internal sealed class <Action><Feature>Endpoint : IEndpoint
   {
       public sealed record Request(string Name, string? Description);

       public void MapEndpoint(IEndpointRouteBuilder app)
       {
           app.MapPost("<feature>s", async (
                   Request request,
                   ICommandHandler<<Action><Feature>Command, Guid> handler,   // ← interface, not concrete class
                   CancellationToken cancellationToken) =>
               {
                   var command = new <Action><Feature>Command(request.Name, request.Description);
                   var result = await handler.Handle(command, cancellationToken);
                   return result.Match(
                       id => Results.Created($"/api/v1/<feature>s/{id}", new { id }),
                       CustomResults.Problem);
               })
               .WithTags(Tags.<Feature>)
               .WithName("<Action><Feature>")
               .RequireAuthorization();      // remove only with conscious justification
       }
   }
   ```

   **Critical:** inject `ICommandHandler<TCommand, TResponse>` — NOT the concrete handler class. The `ValidationDecorator` only intercepts the interface; injecting the concrete bypasses validation and is a silent bug.

5. **Tests**
   - `tests/Application.UnitTests/<Feature>/<Action><Feature>CommandValidatorTests.cs` — use `FluentValidation.TestHelper`
   - `tests/Application.UnitTests/<Feature>/<Action><Feature>CommandHandlerTests.cs` — mock `IApplicationDbContext` via Moq
   - `tests/Web.API.IntegrationTests/Endpoints/<Feature>EndpointTests.cs` — add cases to the existing `IClassFixture<CustomWebApplicationFactory>`

### 5.2 Query / read flow

Same pattern with `IQuery<TResponse>` + `IQueryHandler<TQuery, TResponse>`. **Queries are not validated by the decorator** (only commands). Use EF projection directly into a response DTO — never return domain entities.

```csharp
public async Task<Result<<Feature>Response>> Handle(
    Get<Feature>ByIdQuery query,
    CancellationToken cancellationToken)
{
    <Feature>Response? response = await dbContext.<Feature>s
        .Where(e => e.Id == query.Id && !e.IsDeleted)       // always filter IsDeleted
        .Select(e => new <Feature>Response(e.Id, e.Name))
        .FirstOrDefaultAsync(cancellationToken);

    return response is null
        ? Result.Failure<<Feature>Response>(<Feature>Errors.NotFound(query.Id))
        : response;
}
```

### 5.3 Domain entity + EF configuration

- `src/Domain/<Feature>/<Feature>.cs` — inherits `Entity` (gets `CreatedAt`, `DeletedAt`, `IsDeleted`)
- `src/Domain/<Feature>/<Feature>Errors.cs` — static class with `Error.NotFound`, `Error.Validation`, etc.
- `src/Infra/Config/<Feature>Configuration.cs` — inherits `AbstractConfiguration<<Feature>>`
- `src/Infra/Database/ApplicationDbContext.cs` — add `DbSet<<Feature>> <Feature>s { get; set; }`
- `src/Application/Abstractions/Data/IApplicationDbContext.cs` — expose the same `DbSet`
- Create an EF migration (see §4)

---

## 6. The Result / Error pattern

No exceptions for control flow. Handlers return `Result<T>` or `Result`. Error types in `SharedKernel.Error`:

| Factory | HTTP mapping via `CustomResults.Problem` |
|---|---|
| `Error.Validation(code, desc)` | 400 |
| `Error.NotFound(code, desc)` | 404 |
| `Error.Conflict(code, desc)` | 409 |
| `Error.Forbidden(code, desc)` | 403 |
| `Error.Problem(code, desc)` | 500 |
| `Error.Failure(code, desc)` | 500 |
| `ValidationError(Error[])` | 400 with `errors` extension |

`code` format: `<Feature>.<Rule>` (e.g. `SampleEntity.NotFound`). Define them as `public static readonly` fields or methods on `<Feature>Errors`.

Endpoints map `Result` to HTTP via `result.Match(onSuccess, CustomResults.Problem)` from `Web.API.Extensions.ResultExtensions`.

---

## 7. The validation pipeline

Registered in `Application/DependencyInjection.cs`:

```csharp
services.AddValidatorsFromAssembly(..., includeInternalTypes: true);
services.TryDecorate(typeof(ICommandHandler<,>), typeof(ValidationDecorator.CommandHandler<,>));
services.TryDecorate(typeof(ICommandHandler<>),  typeof(ValidationDecorator.CommandBaseHandler<>));
```

- **`TryDecorate`** (not `Decorate`) is intentional — it won't throw if no handlers of that shape exist.
- Validators run in parallel with `Task.WhenAll` then aggregate failures into a single `ValidationError`.
- Queries are **not** decorated — add input validation in the handler if needed, or introduce a query decorator.

---

## 8. Testing conventions

### Unit tests (`Application.UnitTests`, `Domain.UnitTests`)

- Use `Shouldly` for assertions (`result.IsSuccess.ShouldBeTrue()`).
- Use `Moq` for `IApplicationDbContext`; use EF InMemory (`Microsoft.EntityFrameworkCore.InMemory`) when you need actual `DbSet` query support (see `GetSampleEntityByIdQueryHandlerTests` for the pattern — define a nested `TestDbContext`).
- Test class naming: `<SystemUnderTest>Tests`. Method naming: `Should_<ExpectedOutcome>_When<Condition>` or `<Method>_Should_<Outcome>`.
- AAA (Arrange-Act-Assert) with blank lines between sections.
- No test should touch the network, disk, or real database.

### Integration tests (`Web.API.IntegrationTests`)

- Use `IClassFixture<CustomWebApplicationFactory>` — fixture is shared per class.
- Factory replaces real `DbContext` with EF InMemory and sets env vars (`JWT_SECRET`, `DB_CONNECTION_STRING`) in its constructor **before** `Program.Main` runs.
- Factory exposes `CreateBearerToken(userId)` — use it for authenticated requests.
- When adding new EF-related tests, note the factory strips all `Microsoft.EntityFrameworkCore.*` descriptors to avoid the "multiple providers" error.
- **Always** test: 401 without token, happy path, the main validation failure path, the not-found path.

### Architecture tests

Add new rules to `ArchitectureTests.cs` whenever you introduce a new convention (e.g. "all validators end with `Validator`", "no `DateTime.Now` in Domain").

---

## 9. Security — hard rules

1. **Never** commit secrets. `appsettings.json` has empty strings for `Jwt:Secret` and connection strings by design. Real values come from env vars:
   - `JWT_SECRET` (must be ≥ 32 bytes UTF-8 — `Infra.DependencyInjection` fails at startup otherwise)
   - `DB_CONNECTION_STRING`
   - `RABBITMQ_HOST`, `RABBITMQ_USER`, `RABBITMQ_PASSWORD`
   - **Auth.API**:
     - `AUTH_DB_CONNECTION_STRING` (or `ConnectionStrings__AuthDb`) — dedicated Postgres for the auth bounded context
     - `Redis__ConnectionString` (or `REDIS_CONNECTION_STRING`)
     - `ENTRA_TENANT_ID`, `ENTRA_CLIENT_ID`, `ENTRA_CLIENT_SECRET`, `ENTRA_AUTHORITY` — Microsoft Entra ID (OIDC) federation
     - `OPENIDDICT_BFF_SECRET`, `OPENIDDICT_WEB_API_SECRET`, `OPENIDDICT_GATEWAY_SECRET` — client secrets seeded into OpenIddict applications. **In production these MUST fail-fast at startup if missing or using a default value.**
   - **Gateway**:
     - `Auth__Authority` — Auth.API base URL used for OIDC discovery
     - `Auth__IntrospectionEndpoint` — full URL of `/connect/introspect` (defaults to `{Authority}/connect/introspect`)
     - `Auth__IntrospectionClientId` (defaults to `gateway`) and `Auth__IntrospectionClientSecret` (`OPENIDDICT_GATEWAY_SECRET`) — must match a seeded OpenIddict resource server with the `Endpoints.Introspection` permission
     - `Redis__ConnectionString` — Redis used by `IntrospectionCachingHandler`
     - `IntrospectionCache__TtlSeconds` (defaults to `30`) — TTL for cached introspection responses
     - `ReverseProxy__*` — YARP routes/clusters (see `compose.yaml` for the full env-var-driven config)
   - **Web.Blazor (BFF)** — does NOT need `JWT_SECRET`, `DB_CONNECTION_STRING`, or any RabbitMQ vars. It is an OIDC client only, so it only needs:
     - `Auth__Authority` — Auth.API base URL (e.g. `http://auth.api:8080`)
     - `Auth__ClientId` — must be `bff-blazor` (matches the seeded OpenIddict client)
     - `Auth__ClientSecret` (`OPENIDDICT_BFF_SECRET`) — client secret for code+PKCE+secret
     - `Redis__ConnectionString` — backs the server-side token store, the DataProtection key ring, and the distributed cache
     - `Gateway__BaseUrl` — used by `IAdminGatewayClient` to reach Auth.API admin endpoints (e.g. `http://gateway:8080`)
2. **Every endpoint** gets `.RequireAuthorization()` by default. If an endpoint must be public, call `.AllowAnonymous()` and explain why in the PR.
3. **No raw SQL with string concatenation.** EF Core parameterises automatically; use `FromSqlInterpolated` if you need raw SQL.
4. **Never return domain entities from endpoints.** Always project into a DTO / response record.
5. **Never log PII, passwords, or tokens.** Serilog is structured — scrub sensitive fields explicitly.
6. **GlobalExceptionHandlingMiddleware** must never leak `exception.Message` for unknown exceptions — the generic message is intentional.
7. **CORS**: add origins to `Cors:AllowedOrigins` in config, never use `AllowAnyOrigin()` in code.
8. **Rate limiter**: default is 100 req/min per identity; tune `SecurityExtensions.AddApiRateLimiting` if a specific endpoint needs different limits.
9. **Password hashing**: use the provided `IPasswordHasher` (PBKDF2); never roll your own.

---

## 10. Logging (Serilog)

- Configured in `appsettings.json` under `Serilog`. Sinks: Console + Seq.
- **Always** use structured logging: `logger.LogInformation("User {UserId} did {Action}", userId, action)` — never string interpolation.
- Request context enrichment is wired via `RequestContextLoggingMiddleware` — correlation IDs, user IDs appear automatically.
- For domain events, log at `Information`. Unhandled exceptions are logged at `Error` by `GlobalExceptionHandlingMiddleware`.

---

## 11. Background services

### Messaging (`src/Infra/Messaging`)

Cross-cutting RabbitMQ infrastructure lives here so all four entrypoints share it:

- `RabbitMqOptions` — bound from `RabbitMq:*` config (defaults to `sample.exchange` topic).
- `RabbitMqConnectionFactory` — singleton, lazy-init, caches the `IConnection` for the process.
- `RabbitMqMessagePublisher : IMessagePublisher` — opens an ephemeral channel per publish, declares the exchange (idempotent), serializes payload as persistent JSON.
- Wire it in any entrypoint via `services.AddInfrastructureMessaging(configuration)`. The method validates that `Host` / `User` / `Password` / `ExchangeName` are present — startup fails fast otherwise.
- Consumers (`BackgroundService`-derived classes) live in `src/EntryPoints/Worker/Messaging/`. They resolve `RabbitMqConnectionFactory` from DI and create their own channel for `BasicConsumeAsync`.

### CronJobs (`src/EntryPoints/CronJobs`)

- Inherit `CronBackgroundService` in `Jobs/CronBackgroundService.cs` and override `DoWorkAsync`.
- Register in `Program.cs` with `builder.Services.AddHostedService<YourJob>()`.
- Schedule comes from `CronJobs:<JobName>` config section parsed by Cronos.

### Worker (`src/EntryPoints/Worker`)

- Inherit `BackgroundService`. Example: `Messaging/SampleMessageConsumer.cs`.
- Use the async RabbitMQ.Client 7.x API (`IChannel`, `BasicConsumeAsync`, `AsyncEventingBasicConsumer`).
- Connection config lives in `RabbitMqOptions` bound from `RabbitMq:*` config.

### Auth.API (`src/EntryPoints/Auth.API`)

- Standalone identity service — does **not** share `ApplicationDbContext` with the rest of the scaffold. Owns its own Postgres database (`auth_db`) via `AuthDbContext` in `src/Auth.Infra`, plus a Redis cache for session/token lookups.
- Federates Microsoft Entra ID (OIDC) for human users and issues opaque reference tokens validated through `/connect/introspect`.
- Two hosted services run at startup and seed required state idempotently:
  - `OpenIddictClientSeedHostedService` — seeds the BFF, Web.API, and Gateway OpenIddict applications using the `OPENIDDICT_*_SECRET` env vars.
  - `PermissionSeedHostedService` — seeds default roles + permissions into `auth_db`.
- Architecture rules also cover `Auth.Domain` / `Auth.Application` / `Auth.Infra` — see `tests/Web.API.IntegrationTests/Architecture/ArchitectureTests.cs`.

### Web.Blazor (`src/EntryPoints/Web.Blazor`)

- Blazor Server with Interactive Server render mode (`AddInteractiveServerComponents` + `AddInteractiveServerRenderMode`).
- References `Application` and `Infra` directly so razor components can inject `ICommandHandler<,>` / `IQueryHandler<,>` and dispatch through the same validation pipeline as Web.API.
- Razor components must inject the **handler interface**, never the concrete class — same rule as endpoints (§5.1 step 4).
- Auth/session is up to you; the scaffold wires `AddInfrastructure` (which adds JWT bearer) for parity but you may swap to cookie auth for an interactive UI.

---

## 11.bis Observability — OpenTelemetry

`src/Infra/Observability/OpenTelemetryExtensions.cs` exposes a single composition entrypoint:

```csharp
services.AddOpenTelemetryObservability(
    configuration,
    serviceName: "Web.API",          // becomes service.name resource attribute
    includeAspNetCore: true);        // false for Worker / CronJobs (no HTTP server)
```

What's wired:

- **Tracing**: `AddSource(serviceName)`, EF Core (`Microsoft.EntityFrameworkCore` ActivitySource), Npgsql (`Npgsql.OpenTelemetry`), `HttpClient`. `AspNetCore` instrumentation only when `includeAspNetCore: true`.
- **Metrics**: process / runtime / `HttpClient`. `AspNetCore` metrics only when `includeAspNetCore: true`.
- **Exporter**: OTLP, enabled when `OpenTelemetry:OtlpEndpoint` (or env var `OTEL_EXPORTER_OTLP_ENDPOINT`) is set. No exporter ⇒ providers register but emit nowhere — useful for dev.

Add a service-specific `ActivitySource`:

```csharp
private static readonly ActivitySource Source = new("Web.API");
using Activity? activity = Source.StartActivity("CreateSampleEntity");
```

Custom sources need to be registered with `tracing.AddSource("YourSource")` — extend `OpenTelemetryExtensions` if the source is shared across services.

Local dev: point an OpenTelemetry collector at `http://localhost:4317` and set `OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317`. Or run Jaeger / Aspire dashboard / Seq with the OTLP receiver.

---

## 12. Style & conventions

- `ImplicitUsings` + `Nullable` are enabled solution-wide.
- `file-scoped namespaces` everywhere.
- Records for DTOs / commands / queries / responses (`public sealed record`).
- Classes for handlers (`public sealed class`).
- `internal` for validators, endpoints, and EF configurations (the `InternalsVisibleTo` attribute exposes them to tests).
- Primary constructors are preferred (`public sealed class Foo(IDep dep) : IBar`).
- No `I`-prefix exceptions: interfaces always start with `I`.
- `async` methods take `CancellationToken` as the **last** parameter and pass it to every awaited call.
- SonarAnalyzer runs on build — respect its warnings unless you have reason to suppress.

---

## 13. Common pitfalls (read before fighting the compiler)

| Symptom | Cause | Fix |
|---|---|---|
| Endpoint returns 400 without hitting handler | Expected — validator failed | Check `result.Error` is `ValidationError` |
| Endpoint skips validation entirely | Injected concrete handler class | Inject `ICommandHandler<,>` interface instead |
| `Scrutor.DecorationException: Could not find any registered services` | Used `Decorate` where no services match | Use `TryDecorate` |
| Integration test throws "Multiple database providers registered" | `AddDbContext` called twice without cleanup | Strip all `Microsoft.EntityFrameworkCore.*` descriptors in factory (already done in `CustomWebApplicationFactory`) |
| `Jwt:Secret must be configured` on startup | Env var missing or < 32 bytes | Set `JWT_SECRET` |
| Moq `Can not create proxy for type ... not accessible` | Private/nested test types | Make them `public` or skip Moq (use a hand-rolled stub) |
| Handler is not registered | Returning `internal sealed class` handler | Make it `public sealed class` (Scrutor scans, but DI resolves via the interface) |
| `CS1061: 'Result<T>' does not contain 'Match'` | Missing `using Web.API.Extensions;` | Add the import |
| OpenIddict reference token returns `active=false` on introspection | Token expired or resource server client not seeded | Check token TTL and that the resource server client (`web-api`/`gateway`) is correctly seeded by `OpenIddictClientSeedHostedService` |
| Entra `tid` claim missing on principal | Authority misconfigured or strict issuer validation | Verify `Entra:Authority` and that `ValidateIssuer = false` (we accept multi-tenant tokens and validate via our `Tenants` table) |
| Auth.API fails to start with `OpenIddict has not been registered as a default identifier generator` | Migration not applied | Apply migrations — the `InitialAuthSchema` migration includes the OpenIddict EF tables |
| TestServer cannot reach Entra over HTTPS | OIDC discovery requires HTTPS by default | Set `OpenIdConnectOptions.RequireHttpsMetadata = false` in dev/test only |
| Gateway returns 401 for valid token | Introspection client not seeded or wrong secret | Check `Auth:IntrospectionClientId/Secret` match a seeded resource server with `Endpoints.Introspection` permission |
| Token revoked but Gateway still admits requests | Introspection cache hit (default TTL 30s) | Reduce `IntrospectionCache:TtlSeconds`, or call `/connect/revocation` and wait up to TTL |
| Downstream service can't read identity | Forward headers missing | Look for `X-Forwarded-User` / `X-Forwarded-TenantId` headers (set by `ForwardedIdentityTransform`; Plan 5 reads them in Web.API) |
| Web.Blazor BFF returns 500 on first OIDC callback | OIDC handler refusing the discovery doc over HTTP | Make sure `RequireHttpsMetadata = false` in `BffAuthenticationExtensions` for Dev — Auth.API runs on plain HTTP locally; production must flip this back to `true` |
| `session_id` claim missing on the principal | `OnTokenValidated` did not fire because another `Configure<OpenIdConnectOptions>` overrode the events delegate | The `services.AddOptions<OpenIdConnectOptions>(scheme).Configure<ITokenStore>(...)` call must run **before** any other configure-options call on the same scheme, otherwise the later `Configure` replaces `Events.OnTokenValidated` |
| MudDataGrid loads infinitely on an admin page | Server pagination contract broken | The grid's `ServerData` callback must return a `GridData<T>` whose `TotalItems` matches the upstream `total`. Check `IAdminGatewayClient` actually returns `PagedResponse<T>.Total` — a `0` total leaves MudDataGrid spinning forever |

---

## 14. Forking checklist (when using this scaffold for a new project)

1. Replace `BaseProjectScaffold` with your project name in:
   - `BaseProjectScaffold.sln`
   - `CronJobs.csproj` and `Web.Blazor.csproj` `<UserSecretsId>` and `<AssemblyName>`
   - `README.md`
   - Drop entrypoints you don't need (e.g. delete `Web.Blazor` if API-only) and remove their `compose.yaml` services + Dockerfile path references.
2. Replace `SampleEntity` / `SampleEntities` with your first real aggregate:
   - `src/Domain/SampleEntities/` → `src/Domain/<YourEntity>/`
   - `src/Application/SampleEntities/` → `src/Application/<YourEntity>/`
   - `src/Infra/Config/SampleEntityConfiguration.cs`
   - Endpoints under `src/EntryPoints/Web.API/Endpoints/SampleEntity/`
   - Tests in all three test projects
   - `IApplicationDbContext.SampleEntities` and `ApplicationDbContext.SampleEntities`
3. Update `Jwt:Issuer` / `Jwt:Audience` defaults.
4. Update `compose.yaml` service names, DB name, ports.
5. Generate a fresh initial migration.
6. Rewrite `README.md` for the new project (keep the shape: EN + PT + Mermaid flow).
7. Update this `CLAUDE.md` — remove scaffold-specific notes, keep rules that still apply.

---

## 15. When in doubt

- Prefer **deleting** placeholder code over adapting it — the scaffold is a skeleton, not a library.
- If a rule in this file conflicts with the user's explicit request, **ask before bending it**.
- If you discover a convention that isn't written here, **add it to §12 or §13** in the same PR.
- Don't invent abstractions ahead of real need. Three similar handlers is fine; add a base class only when the fourth shows up and the repetition hurts.

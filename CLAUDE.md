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
- Entrypoints: `Web.API` (HTTP/JSON), `Web.Blazor` (Blazor Server UI), `Worker` (RabbitMQ consumer), `CronJobs` (Cronos scheduler).

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
    ├── Web.Blazor/                  # Blazor Server (interactive server components) — same Infra/Application stack as Web.API
    │   ├── Components/              # Razor components (App, Routes, Layout, Pages)
    │   ├── wwwroot/                 # Static assets
    │   ├── Program.cs               # Composition root (uses AddInfrastructure + AddOpenTelemetryObservability)
    │   ├── Dockerfile
    │   ├── appsettings.json
    │   └── appsettings.Development.json
    ├── CronJobs/                    # BackgroundService + Cronos scheduler — internal polling, publishes events
    │   ├── Jobs/                    # CronBackgroundService, SampleCronJob, SamplePollingJob (publish example)
    │   └── Dockerfile
    └── Worker/                      # RabbitMQ consumer — extend with one consumer per queue/topic
        ├── Messaging/               # SampleMessageConsumer (uses Infra.Messaging primitives)
        └── Dockerfile

tests/
├── Domain.UnitTests/                # Pure domain tests
├── Application.UnitTests/
│   ├── Behaviors/                   # ValidationDecoratorTests
│   └── SampleEntities/              # Handler + Validator tests
└── Web.API.IntegrationTests/
    ├── Architecture/                # NetArchTest rules
    ├── Endpoints/                   # End-to-end via WebApplicationFactory
    ├── Infrastructure/              # CustomWebApplicationFactory (EF InMemory + JWT)
    └── Middleware/                  # GlobalExceptionHandlingMiddleware tests
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

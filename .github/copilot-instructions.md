# GitHub Copilot Instructions

The authoritative guidance for AI agents in this repository lives in [`../CLAUDE.md`](../CLAUDE.md). Copilot should follow the architecture rules, layered dependency constraints, handler/endpoint conventions, validation pipeline, observability wiring, testing conventions, and security rules defined there.

## Key invariants (see `CLAUDE.md` for full details)

- **.NET 10 (LTS)**, C# 14, Central Package Management. SDK pinned via `global.json` (`10.0.203`).
- **Four entrypoints** under `src/EntryPoints/`: `Web.API`, `Web.Blazor`, `CronJobs`, `Worker`. All consume `Infra` for DB, auth, RabbitMQ, and OpenTelemetry.
- **Clean Architecture** dependency rules are enforced by `tests/Web.API.IntegrationTests/Architecture/ArchitectureTests.cs` — do not break them.
- All command/query handlers and endpoints must be `sealed`.
- Endpoints (and Razor components in Web.Blazor) inject the handler **interface** (`ICommandHandler<T, TResp>` / `IQueryHandler<T, TResp>`), never the concrete class — otherwise the `ValidationDecorator` is bypassed.
- Concrete `IMessagePublisher` implementations must live in `Infra` (never in an entrypoint).
- **Observability**: every entrypoint wires `services.AddOpenTelemetryObservability(config, serviceName, includeAspNetCore)` from `Infra.Observability`. OTLP exporter activates when `OpenTelemetry:OtlpEndpoint` or `OTEL_EXPORTER_OTLP_ENDPOINT` is set.
- Secrets come from env vars (`JWT_SECRET`, `DB_CONNECTION_STRING`, `RABBITMQ_*`). `appsettings.json` ships with empty values.
- Every Web.API endpoint is `.RequireAuthorization()` by default.
- Use the `Result<T>` / `Error` pattern; never throw for control flow.
- Tests use xUnit + Shouldly + Moq + FluentValidation.TestHelper + NetArchTest (35 tests across 3 suites).
- NuGet uses a repo-local `.nuget-cache/` (configured in `nuget.config`, gitignored).

For anything not listed here, read `CLAUDE.md` end-to-end before suggesting changes.

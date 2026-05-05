# AGENTS.md

AI coding agents working on this repository should read [`CLAUDE.md`](./CLAUDE.md) — it is the authoritative, complete instruction set (architecture, conventions, testing, security, observability, forking checklist, common pitfalls).

This file exists so tools that look specifically for `AGENTS.md` (Cursor, Aider, OpenAI Codex CLI, and emerging agent frameworks) pick up the same guidance as Claude Code.

## Quick reference (full details in `CLAUDE.md`)

- **Stack**: .NET 10 (LTS), C# 14, Central Package Management. SDK pinned by `global.json` to `10.0.203`.
- **Entrypoints** (`src/EntryPoints/`): `Web.API` (Minimal APIs), `Web.Blazor` (Blazor Server), `CronJobs` (Cronos scheduler), `Worker` (RabbitMQ consumer). All four share `Infra` for DB, auth, messaging, and OpenTelemetry.
- **Layering**: `Domain → SharedKernel`; `Application → Domain`; `Infra → Application`; entrypoints → `Infra`. Enforced by `tests/Web.API.IntegrationTests/Architecture/ArchitectureTests.cs`.
- **Handlers / endpoints must be `sealed`.** Endpoints inject the **handler interface** (`ICommandHandler<,>`), never the concrete class — otherwise the `ValidationDecorator` is bypassed.
- **`Result<T>` / `Error` pattern** — never throw for control flow.
- **Secrets via env vars** (`JWT_SECRET`, `DB_CONNECTION_STRING`, `RABBITMQ_*`, `OTEL_EXPORTER_OTLP_ENDPOINT`). `appsettings.json` ships empty.
- **Every Web.API endpoint is `.RequireAuthorization()` by default.**
- **Tests**: xUnit + Shouldly + Moq + FluentValidation.TestHelper + NetArchTest. 35 tests across 3 suites. Integration tests use EF InMemory + signed test JWT — no external services.
- **NuGet** restores into a repo-local `.nuget-cache/` (gitignored, configured in `nuget.config`).

**Do not duplicate content here.** Update `CLAUDE.md` instead — this file should remain a thin pointer + cheat sheet.

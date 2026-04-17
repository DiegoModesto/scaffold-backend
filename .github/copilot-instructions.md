# GitHub Copilot Instructions

The authoritative guidance for AI agents in this repository lives in [`../CLAUDE.md`](../CLAUDE.md). Copilot should follow the architecture rules, layered dependency constraints, handler/endpoint conventions, validation pipeline, testing conventions, and security rules defined there.

Key invariants (see `CLAUDE.md` for full details):

- Clean Architecture dependency rules are enforced by `tests/Web.API.IntegrationTests/Architecture/ArchitectureTests.cs` — do not break them.
- All command/query handlers and endpoints must be `sealed`.
- Endpoints inject the handler **interface** (`ICommandHandler<T, TResp>`), never the concrete class — otherwise the `ValidationDecorator` is bypassed.
- Secrets come from env vars (`JWT_SECRET`, `DB_CONNECTION_STRING`). `appsettings.json` ships with empty values.
- Every endpoint is `.RequireAuthorization()` by default.
- Use the `Result<T>` / `Error` pattern; never throw for control flow.
- Tests use xUnit + Shouldly + Moq + FluentValidation.TestHelper + NetArchTest.

For anything not listed here, read `CLAUDE.md` end-to-end before suggesting changes.

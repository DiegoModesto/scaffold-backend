# Design — Rich Domain & Value Objects

**Date:** 2026-09-02
**Status:** Approved (design phase)
**Topic:** Replace primitive obsession across `Domain` and `Auth.Domain` with strongly-typed
IDs and business value objects, make aggregates rich (no public setters, no anemic entities),
and encode the convention as executable architecture rules.

---

## 1. Goal

This repository is a **template**. Its aggregates are the example every fork copies without
thinking. Today `Domain/SampleEntities/SampleEntity` is anemic — `public set` on every
property, no factory, no invariant — and `Auth.Domain`, while behaviourally rich, models every
concept as `string` or `Guid`. Nothing prevents passing a `groupId` where a `roleId` is
expected, or constructing a `User` with an empty email.

The goal is to make the illegal state unrepresentable, and to make the convention survive
contact with future PRs by enforcing it in `ArchitectureTests` rather than in prose.

**Non-goal:** a general-purpose DDD framework. Every abstraction below exists because a
concrete line of code in this repo needs it.

## 2. Current state (verified)

Read from the working tree on 2026-09-02, not assumed.

- **Primary constructors are already done.** The only remaining traditional constructors are
  legitimate and must NOT be "fixed": EF's private parameterless constructors on the seven
  Auth aggregates, the seven FluentValidation validators (no dependencies to inject), and
  `Result` / `Error` / `ValidationError` / `ErrorType` in `SharedKernel` (guard logic in the
  body). Handlers and services already use primary constructors per CLAUDE.md §12.
- `SharedKernel/Entity.cs` exposes `CreatedAt`, `DeletedAt` and `IsDeleted` with **public
  setters** — any caller can un-delete an aggregate. `Role.Delete()` and `Group.Delete()`
  duplicate the same two-line body.
- `Domain/SampleEntities/SampleEntity` is anemic: `Guid Id`, `Guid TenantId`, `string Name`,
  `string? Description`, all `public get; set;`.
- `Auth.Domain` has 7 aggregates (`Tenant`, `User`, `Group`, `Role`, `Permission`,
  `M2MClient`, `AuthAuditEvent`) with private setters, static factories and behaviour methods
  — but 100% primitive property types, including `IReadOnlyCollection<Guid> RoleIds` /
  `GroupIds` / `PermissionIds`.
- `Auth.Domain/Permissions/PermissionCodes.All` is
  `IReadOnlyCollection<(string Code, string Description)>`; permission codes travel as bare
  strings all the way to `IPermissionResolver`.
- Packages pinned in `Directory.Packages.props`: EF Core **10.0.7**, Npgsql EF **10.0.1**,
  `EFCore.NamingConventions` **10.0.1**, FluentValidation **12.1.1**, NetArchTest **1.3.2**.
- **Both** DbContexts call `UseSnakeCaseNamingConvention()` (`src/Infra/DependencyInjection.cs`,
  `src/Auth.Infra/DependencyInjection.cs`, `AuthDbContextDesignTimeFactory`).
- Existing migrations: `20260507164641_InitialAuthSchema`, `20260507220535_SampleEntityTenantId`.
- `ArchitectureTests` has 10 rules today: layering, sealed handlers/endpoints, and
  `IMessagePublisher` placement. **No rule constrains domain shape.**

### 2.1 Three traps found in the current code

These are not hypothetical. They are why this refactor needs phases and tests rather than a
mechanical find-and-replace.

1. **`AuthDbContext.EnforceTenantGuard` degrades silently.** It reads
   `entry.Property("TenantId").CurrentValue` and does `if (current is not Guid currentTenantId)
   { continue; }`. Once `TenantId` is a strongly-typed struct the pattern match fails, the loop
   `continue`s for every entity, and **cross-tenant writes stop being blocked**. There is no
   test covering this guard today, so nothing would go red. This is the highest-risk item in
   the whole refactor.
2. **`AuthDbContext.ReconcileAsync<TJoin>` is hardcoded to `Guid`** — `HashSet<Guid>`,
   `Func<Guid, TJoin>`, `Func<TJoin, Guid>`. It must become generic over the id type, and the
   join entities (`UserRole.UserId`/`RoleId`, `UserGroup`, `GroupRole`, `RolePermission`) need
   the same converters.
3. **snake_case naming turns a complex property into a new column.** With
   `EFCore.NamingConventions` active, a complex type `Email` with member `Value` maps to
   `email_value`, not `email` — which would generate a rename migration on every value object.
   Every complex property therefore needs an explicit `HasColumnName`.

## 3. Decisions

| # | Decision | Rationale |
|---|---|---|
| 1 | Scope covers `Domain` **and** `Auth.Domain` | Auth is the code that actually runs; the template alone would leave the pattern unproven |
| 2 | Strongly-typed IDs (`readonly record struct`) | Makes `AssignRole(groupId)` a compile error |
| 3 | VO validates via `Result<T>` **and** FluentValidation stays on commands | Validator produces an aggregated 400; the VO guarantees the invariant |
| 4 | `ComplexProperty` for business VOs, `ValueConverter` for IDs | See §6 |
| 5 | Fix the tenant guard, regression test written first | See §7 |
| 6 | Phased execution, green build + empty migration diff per phase | See §11 |

### 3.1 No `ValueObject` base class

Deliberately omitted. `record` and `readonly record struct` already provide structural
equality, `GetHashCode`, `ToString` and `Deconstruct`. An abstract base with
`GetEqualityComponents()` is a pre-record C# idiom that would add a layer without adding a
guarantee. YAGNI.

## 4. SharedKernel primitives

```csharp
// SharedKernel/IStronglyTypedId.cs
public interface IStronglyTypedId<out TValue> where TValue : struct
{
    TValue Value { get; }
}
```

Its only jobs are to drive automatic `ValueConverter` registration (§6) and to give the
`JsonConverterFactory` (§8) and the architecture tests (§10) something to match on.

`Entity` loses its public setters and absorbs the duplicated soft-delete logic:

```csharp
public abstract class Entity
{
    public DateTimeOffset CreatedAt { get; protected set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    public bool IsDeleted { get; private set; }

    protected void MarkCreated() => CreatedAt = DateTimeOffset.UtcNow;

    public void MarkDeleted()
    {
        IsDeleted = true;
        DeletedAt = DateTimeOffset.UtcNow;
    }
}
```

`Role.Delete()` and `Group.Delete()` are deleted in favour of `MarkDeleted()`.

## 5. The two canonical shapes

The distinction between these two forms is the rule that goes into CLAUDE.md §12.

### 5.1 Strongly-typed ID — `readonly record struct`, no validation

```csharp
public readonly record struct UserId(Guid Value) : IStronglyTypedId<Guid>, IParsable<UserId>
{
    public static UserId New() => new(Guid.CreateVersion7());
    public static UserId From(Guid value) => new(value);
    public override string ToString() => Value.ToString();

    public static UserId Parse(string s, IFormatProvider? provider) => new(Guid.Parse(s));
    public static bool TryParse(string? s, IFormatProvider? provider, out UserId result)
    {
        if (Guid.TryParse(s, out Guid value)) { result = new UserId(value); return true; }
        result = default;
        return false;
    }
}
```

`Guid.CreateVersion7()` replaces `Guid.NewGuid()` — time-ordered ids reduce index
fragmentation on the Postgres primary keys. No validation: an id is opaque.

### 5.2 Business value object — sealed `record`, private ctor, `Create` returning `Result<T>`

```csharp
public sealed record Email
{
    public const int MaxLength = 320;

    private Email(string value) => Value = value;

    public string Value { get; private init; }

    public static Result<Email> Create(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))     return Result.Failure<Email>(EmailErrors.Empty);
        if (input.Length > MaxLength)             return Result.Failure<Email>(EmailErrors.TooLong);
        if (!input.Contains('@', StringComparison.Ordinal))
                                                  return Result.Failure<Email>(EmailErrors.Invalid);

        return new Email(input.Trim().ToLowerInvariant());
    }
}
```

Returning `Result<T>` rather than throwing keeps CLAUDE.md §6 intact (no exceptions for
control flow). The private constructor makes an invalid instance unrepresentable. `MaxLength`
is public so the FluentValidation validator can mirror the rule without restating the number.

## 6. EF Core mapping

Two mechanisms, chosen for a concrete reason rather than uniformity.

**IDs use `ValueConverter`.** They are single-field, they participate in keys and foreign keys,
and no query ever needs to reach *inside* them.

```csharp
builder.Property(u => u.Id).HasConversion(id => id.Value, value => UserId.From(value));
```

**Business VOs use `ComplexProperty`.** A `ValueConverter` is opaque to the LINQ translator:
`ListUsersQueryHandler` does `u.Email.Contains(s)`, which would throw at runtime under a
converter and force an `EF.Property<string>(u, "Email")` escape hatch at every text filter.
A complex type keeps the member visible to the translator, so `u.Email.Value.Contains(s)`
still becomes SQL.

```csharp
builder.ComplexProperty(u => u.Email, b => b
    .Property(e => e.Value)
    .HasColumnName("email")          // REQUIRED — snake_case would produce "email_value"
    .HasMaxLength(Email.MaxLength)
    .IsRequired());
```

`OwnsOne` was considered and rejected: an owned type is an entity, carrying identity and change
tracking, and tends toward nullable/renamed columns. Complex types exist in EF 10 precisely to
replace that usage.

**Verification target: `dotnet ef migrations add` must produce an empty diff.** Converters map
to the same underlying column type and complex properties are pinned with `HasColumnName`, so
no schema changes. If a migration is non-empty, the mapping is wrong — this is the check that
closes every phase.

## 7. The two dangerous spots

### 7.1 Tenant guard

The regression test is written **first**, while the guard still works, and must go red the
moment the guard is silenced:

> Given an `AuthDbContext` whose `ITenantContext` reports tenant A, when an entity carrying
> tenant B is added and `SaveChangesAsync` is called, then `InvalidOperationException` is
> thrown.

The guard is then rewritten around a marker interface rather than a string lookup:

```csharp
public interface ITenantOwned
{
    TenantId TenantId { get; }
}
```

Implemented by every multi-tenant aggregate. This replaces `FindProperty("TenantId")` plus a
runtime type check with a compiler-checked contract, and lets an architecture rule assert that
no multi-tenant aggregate was forgotten. `AuthAuditEvent` keeps its documented exemption from
the guard.

### 7.2 Membership reconciliation

`ReconcileAsync<TJoin>` becomes `ReconcileAsync<TJoin, TId> where TId : struct`, and the four
join entities get id converters in their configurations.

## 8. The boundary: HTTP, JSON, DTOs

Strongly-typed ids must not leak their shape into the public contract.

- **Route binding** — `UserId` implements `IParsable<UserId>`, so
  `MapGet("users/{id}", (UserId id) => ...)` binds with no custom converter.
- **JSON** — one `JsonConverterFactory` in `SharedKernel` serializes any
  `IStronglyTypedId<Guid>` as a bare GUID. **The HTTP contract and the Blazor admin UI do not
  change.** No `{"value":"..."}` ever appears on the wire.
- **Response DTOs keep primitives.** `UserSummary(Guid Id, string Email, ...)` is unchanged; a
  VO is a domain concept, a DTO is a boundary contract, and merging them drags API versioning
  into the domain. Projections gain `.Value` — which still translates, per §6.

## 9. Validation and errors

Each VO's `Create` returns `Result<T>` with codes in the existing `<Feature>.<Rule>` format
(`Email.Invalid`, `RoleName.TooLong`). FluentValidation **stays** on commands, mirroring the
rule through the VO's public constants.

The duplication is intentional and gets documented as such: **the validator produces a good
400 with all failures aggregated; the VO guarantees the invariant even when a caller bypasses
the validator** (a background job, a seeder, a future gRPC entrypoint).

Handlers gain a composition step: several `Result<VO>` must be combined before constructing
the aggregate. If that turns verbose in three or more handlers, add a `Result.Combine` helper
— **not before**, per CLAUDE.md §15.

## 10. Making the convention permanent

New rules in `tests/Web.API.IntegrationTests/Architecture/ArchitectureTests.cs`:

1. No type in `Domain` / `Auth.Domain` exposes a property with a public setter.
2. Every aggregate has a non-public constructor.
3. No public property on an aggregate is `string` or `Guid` — with an explicit allowlist, so
   an exception costs a deliberate edit and a justification.
4. Every `IStronglyTypedId` implementation is a `readonly record struct`.
5. Every multi-tenant aggregate implements `ITenantOwned`.

Documentation updated in the same PR as the code:

- **CLAUDE.md §5** — the canonical use-case flow rewritten with value objects.
- **CLAUDE.md §12** — the ID-vs-VO rule, and an explicit note that the remaining traditional constructors
  (EF, validators, `Result`/`Error`) are correct as they are.
- **CLAUDE.md §13** — the three traps: the `email_value` column, `Contains` under a converter, the tenant
  guard.

## 11. Phases

Each phase ends with `dotnet build`, `dotnet test`, and `dotnet ef migrations add` yielding an
empty diff. No phase starts with the previous one red.

| # | Phase | Exit criterion |
|---|---|---|
| 0 | EF spike: optional complex type + private-ctor binding | Throwaway test resolves this document's §12 |
| 1 | `SharedKernel`: `Entity`, `IStronglyTypedId`, `JsonConverterFactory` | Build + tests green |
| 2 | `SampleEntity` end to end — the template's canonical example | Empty migration diff |
| 3 | Tenant-guard regression test, **written to fail** | Red on purpose |
| 4 | `Tenant`, `Permission` | Empty diff, green |
| 5 | `Role`, `Group` | Empty diff, green |
| 6 | `User` | Empty diff, green |
| 7 | `M2MClient` | Empty diff, green |
| 8 | `AuthAuditEvent` | Empty diff, green |
| 9 | Typed tenant guard + `ReconcileAsync<TJoin, TId>` | Phase 3's test goes green |
| 10 | Architecture rules + CLAUDE.md | Full suite green |

**Honest cost note.** Phases 4–8 are the bulk of the work and touch roughly 40 handlers plus
the test suite. The durable value is concentrated in phases 1–3 and 10 — the example and the
executable rules. If phases 4–8 stop paying for themselves, stopping after a partial phase 8
leaves the pattern established and enforced rather than half-done.

## 12. Open questions (resolved in phase 0)

Both are stated as unknowns rather than assumed, because guessing wrong invalidates this
document's §6.

1. **Optional complex types.** `User.NetSuiteEmail` and `Group.EntraGroupId` are nullable. EF
   Core 8 and 9 did not support optional complex properties; EF 10 improved this area, but the
   exact behaviour on 10.0.7 with Npgsql must be proven. **Fallback:** nullable VOs use
   `ValueConverter`, and the CLAUDE.md §12 rule gains a documented exception.
2. **Private-constructor binding.** If EF cannot materialize a complex type through a private
   constructor, the VO exposes an `internal` constructor and `Auth.Infra` / `Infra` are added
   to `InternalsVisibleTo`.

## 13. Out of scope (YAGNI)

- Domain events / an outbox. Unrelated to this refactor.
- A `ValueObject` base class (§3.1).
- Changing the public HTTP contract or the Blazor admin UI (§8).
- `Result.Combine`, unless §9's threshold is actually reached.
- The GraphQL entrypoint and the process-catalogue documentation — separate specs, executed
  after this one.

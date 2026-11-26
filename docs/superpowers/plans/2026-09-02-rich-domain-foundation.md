# Rich Domain Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Establish the value-object foundation in `SharedKernel` and prove it end to end on `SampleEntity`, the template's canonical example, without changing any HTTP contract or database schema.

**Architecture:** `SharedKernel` gains a strongly-typed-id contract and a JSON converter so typed ids serialize as bare GUIDs. `Entity` loses its public setters. `SampleEntity` becomes a rich aggregate with a private constructor, a static factory, a `SampleEntityId`, a shared `TenantId`, and a `SampleEntityName` value object that validates through `Result<T>`. EF maps ids with `ValueConverter` and business value objects with `ComplexProperty` pinned to the existing column names.

**Tech Stack:** .NET 10 (`net10.0`), EF Core 10.0.7, Npgsql EF 10.0.1, `EFCore.NamingConventions` 10.0.1 (snake_case), FluentValidation 12.1.1, xunit + Shouldly + Moq, NetArchTest 1.3.2.

**Spec:** `docs/superpowers/specs/2026-09-02-rich-domain-value-objects-design.md`

## Global Constraints

- Target framework `net10.0`; all package versions come from `Directory.Packages.props` (Central Package Management) — never pin a version in a `.csproj`.
- **No schema change.** `dotnet ef migrations add` must produce an empty diff at the end of every task that touches EF mapping.
- **No HTTP contract change.** Request and response JSON stay byte-identical; typed ids serialize as bare GUIDs.
- Every complex property needs an explicit `HasColumnName` — snake_case naming would otherwise map `Name` to `name_value`.
- Value objects never throw; `Create` returns `Result<T>` (CLAUDE.md §6).
- Handlers stay `public sealed`; validators and EF configurations stay `internal` (CLAUDE.md §12).
- `async` methods take `CancellationToken` last and pass it to every awaited call.
- Commit after every task. Run `dotnet build BaseProjectScaffold.sln` and `dotnet test BaseProjectScaffold.sln` before each commit.

## Deviations from the spec (decided while planning, applied here)

Three refinements. Each is a deliberate narrowing, recorded so the spec can be amended.

1. **`Description` stays a plain `string?`.** The spec's catalogue listed it as a value object, but its only rule is a max length. Wrapping it buys no invariant and costs projection friction. Value objects are for concepts with real rules — that is the example the template should set.
2. **`TenantId` lives in `SharedKernel`, not `Domain`.** Both `Domain` and `Auth.Domain` need it and neither may reference the other.
3. **`IStronglyTypedId` takes a self type.** The spec sketched `IStronglyTypedId<out TValue>`, which exposes `Value` but cannot construct — the JSON converter needs to build an id from a GUID. The interface therefore carries a `static abstract From`, which variance forbids.
4. **Phase 3's test passes when written.** The spec's phase table says "written to fail — red on purpose", contradicting its own §7.1. The guard works today, so a correct regression test is green on arrival and only turns red if someone silences the guard. §7.1 is right; the table is wrong.

---

### Task 1: EF spike — prove complex-type mapping before relying on it

Throwaway. Its output is an answer recorded in the commit message; the code is deleted in the last step.

**Files:**
- Create (temporary): `tests/Web.API.IntegrationTests/Spikes/ComplexTypeSpikeTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: a documented answer to two questions — whether EF Core 10.0.7 materializes a complex type through a **private constructor**, and whether `HasColumnName` defeats the snake_case convention. No code survives.

- [ ] **Step 1: Write the spike test**

Model-building only — no database is needed, which is why this is fast.

```csharp
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Web.API.IntegrationTests.Spikes;

public class ComplexTypeSpikeTests
{
    private sealed record Probe
    {
        private Probe(string value) => Value = value;
        public string Value { get; private init; }
        public static Probe Create(string value) => new(value);
    }

    private sealed class Holder
    {
        public Guid Id { get; private set; }
        public Probe Name { get; private set; } = Probe.Create("x");
    }

    private sealed class SpikeContext(DbContextOptions<SpikeContext> options) : DbContext(options)
    {
        public DbSet<Holder> Holders => Set<Holder>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Holder>(b =>
            {
                b.ToTable("holders");
                b.HasKey(h => h.Id);
                b.ComplexProperty(h => h.Name, n => n
                    .Property(p => p.Value)
                    .HasColumnName("name")
                    .IsRequired());
            });
        }
    }

    [Fact]
    public void ComplexProperty_Should_BindPrivateConstructor_And_UseExplicitColumnName()
    {
        var options = new DbContextOptionsBuilder<SpikeContext>()
            .UseNpgsql("Host=localhost;Database=spike;Username=spike;Password=spike")
            .UseSnakeCaseNamingConvention()
            .Options;

        using var context = new SpikeContext(options);

        var entityType = context.Model.FindEntityType(typeof(Holder))!;
        var complex = entityType.GetComplexProperties().Single();
        var column = complex.ComplexType.GetProperties().Single();

        column.GetColumnName().ShouldBe("name");
    }
}
```

- [ ] **Step 2: Run the spike**

Run: `dotnet test tests/Web.API.IntegrationTests --filter "FullyQualifiedName~ComplexTypeSpikeTests" -v n`

Two possible outcomes, both useful:
- **PASS** — complex types bind through a private constructor and `HasColumnName` wins over snake_case. Proceed as planned.
- **FAIL at model build** — record the exact exception. If it names the constructor, every value object gains an `internal` constructor plus `InternalsVisibleTo` for `Infra` / `Auth.Infra`. If the column resolves to `name_value`, `HasColumnName` is being applied at the wrong level and must move onto the complex builder.

- [ ] **Step 3: Record the answer and delete the spike**

```bash
rm -rf tests/Web.API.IntegrationTests/Spikes
```

- [ ] **Step 4: Commit the finding**

Replace `<OUTCOME>` with one or two sentences stating what the run proved.

```bash
git add -A
git commit -m "chore: EF complex-type spike — <OUTCOME>"
```

---

### Task 2: `IStronglyTypedId` and JSON serialization

Purely additive — nothing in the solution references these yet, so the build stays green throughout.

**Files:**
- Create: `src/SharedKernel/IStronglyTypedId.cs`
- Create: `src/SharedKernel/Json/StronglyTypedIdJsonConverter.cs`
- Create: `tests/Domain.UnitTests/SharedKernel/StronglyTypedIdJsonConverterTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `SharedKernel.IStronglyTypedId<TSelf, TValue>` with instance property `TValue Value { get; }` and `static abstract TSelf From(TValue value)`.
  - `SharedKernel.Json.StronglyTypedIdJsonConverterFactory : JsonConverterFactory` — parameterless, registered into `JsonSerializerOptions.Converters`.

- [ ] **Step 1: Write the failing test**

Create `tests/Domain.UnitTests/SharedKernel/StronglyTypedIdJsonConverterTests.cs`:

```csharp
using System.Text.Json;
using SharedKernel;
using SharedKernel.Json;
using Shouldly;

namespace Domain.UnitTests.SharedKernel;

public class StronglyTypedIdJsonConverterTests
{
    private readonly record struct ProbeId(Guid Value) : IStronglyTypedId<ProbeId, Guid>
    {
        public static ProbeId From(Guid value) => new(value);
    }

    private static JsonSerializerOptions Options() =>
        new() { Converters = { new StronglyTypedIdJsonConverterFactory() } };

    [Fact]
    public void Serialize_Should_WriteBareGuid_NotAnObject()
    {
        var id = new ProbeId(Guid.Parse("11111111-1111-1111-1111-111111111111"));

        string json = JsonSerializer.Serialize(id, Options());

        json.ShouldBe("\"11111111-1111-1111-1111-111111111111\"");
    }

    [Fact]
    public void Deserialize_Should_ReadBareGuid()
    {
        const string json = "\"22222222-2222-2222-2222-222222222222\"";

        var id = JsonSerializer.Deserialize<ProbeId>(json, Options());

        id.Value.ShouldBe(Guid.Parse("22222222-2222-2222-2222-222222222222"));
    }

    [Fact]
    public void RoundTrip_Should_PreserveValue()
    {
        var original = new ProbeId(Guid.CreateVersion7());

        string json = JsonSerializer.Serialize(original, Options());
        var restored = JsonSerializer.Deserialize<ProbeId>(json, Options());

        restored.ShouldBe(original);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Domain.UnitTests --filter "FullyQualifiedName~StronglyTypedIdJsonConverterTests"`
Expected: FAIL — compile error, `IStronglyTypedId` and `StronglyTypedIdJsonConverterFactory` do not exist.

- [ ] **Step 3: Write the interface**

Create `src/SharedKernel/IStronglyTypedId.cs`:

```csharp
namespace SharedKernel;

/// <summary>
/// Contract for a single-field identifier wrapper. <typeparamref name="TSelf"/> is the
/// implementing struct, which lets converters build an id from its underlying value.
/// </summary>
public interface IStronglyTypedId<TSelf, TValue>
    where TSelf : struct, IStronglyTypedId<TSelf, TValue>
    where TValue : struct
{
    TValue Value { get; }

    static abstract TSelf From(TValue value);
}
```

- [ ] **Step 4: Write the converter**

Create `src/SharedKernel/Json/StronglyTypedIdJsonConverter.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SharedKernel.Json;

/// <summary>
/// Serializes any <see cref="IStronglyTypedId{TSelf,TValue}"/> as its bare underlying value,
/// so a typed id never leaks its wrapper shape into the HTTP contract.
/// </summary>
public sealed class StronglyTypedIdJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert.IsValueType && FindContract(typeToConvert) is not null;

    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        Type contract = FindContract(typeToConvert)
            ?? throw new InvalidOperationException($"'{typeToConvert}' is not a strongly-typed id.");

        Type valueType = contract.GetGenericArguments()[1];
        Type converterType = typeof(StronglyTypedIdJsonConverter<,>)
            .MakeGenericType(typeToConvert, valueType);

        return (JsonConverter?)Activator.CreateInstance(converterType);
    }

    private static Type? FindContract(Type type) =>
        Array.Find(
            type.GetInterfaces(),
            i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IStronglyTypedId<,>));
}

internal sealed class StronglyTypedIdJsonConverter<TId, TValue> : JsonConverter<TId>
    where TId : struct, IStronglyTypedId<TId, TValue>
    where TValue : struct
{
    public override TId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        TId.From(JsonSerializer.Deserialize<TValue>(ref reader, options));

    public override void Write(Utf8JsonWriter writer, TId value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, value.Value, options);
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/Domain.UnitTests --filter "FullyQualifiedName~StronglyTypedIdJsonConverterTests"`
Expected: PASS, 3 tests.

- [ ] **Step 6: Commit**

```bash
git add src/SharedKernel/IStronglyTypedId.cs src/SharedKernel/Json tests/Domain.UnitTests/SharedKernel
git commit -m "feat(shared-kernel): add strongly-typed id contract and JSON converter"
```

---

### Task 3: Harden `Entity` and give aggregates ownership of their lifecycle

`Entity`'s public setters disappear, which breaks three call sites. All of them are fixed here so the build never goes red across a commit boundary. Still no value objects — that is Task 4.

**Files:**
- Modify: `src/SharedKernel/Entity.cs`
- Modify: `src/Domain/SampleEntities/SampleEntity.cs`
- Modify: `src/Application/SampleEntities/Create/CreateSampleEntityCommandHandler.cs`
- Modify: `src/Auth.Domain/Roles/Role.cs` (remove `Delete()`)
- Modify: `src/Auth.Domain/Groups/Group.cs` (remove `Delete()`)
- Modify: `src/Auth.Application/Admin/Roles/Delete/DeleteRoleCommandHandler.cs:25`
- Modify: `src/Auth.Application/Admin/Groups/Delete/DeleteGroupCommandHandler.cs:25`
- Modify: `tests/Domain.UnitTests/SampleEntities/SampleEntityTests.cs`
- Modify: `tests/Auth.Domain.UnitTests/Roles/RoleTests.cs:49`
- Modify: `tests/Auth.Domain.UnitTests/Groups/GroupTests.cs:37`
- Modify: `tests/Application.UnitTests/SampleEntities/GetSampleEntityByIdQueryHandlerTests.cs`

**Interfaces:**
- Consumes: nothing from Task 2.
- Produces:
  - `SharedKernel.Entity` — `CreatedAt` (`protected set`), `DeletedAt` / `IsDeleted` (`private set`), `protected void MarkCreated()`, `public void MarkDeleted()`.
  - `Domain.SampleEntities.SampleEntity.Create(Guid tenantId, string name, string? description)` returning `SampleEntity`. Ids and names are still primitives at this point; Task 4 changes the signature.

- [ ] **Step 1: Write the failing test**

Replace the whole of `tests/Domain.UnitTests/SampleEntities/SampleEntityTests.cs`:

```csharp
using Domain.SampleEntities;
using Shouldly;

namespace Domain.UnitTests.SampleEntities;

public class SampleEntityTests
{
    [Fact]
    public void Create_Should_SetCreatedAt_AndNotBeDeleted()
    {
        var tenantId = Guid.CreateVersion7();

        SampleEntity entity = SampleEntity.Create(tenantId, "foo", "bar");

        entity.Name.ShouldBe("foo");
        entity.Description.ShouldBe("bar");
        entity.TenantId.ShouldBe(tenantId);
        entity.Id.ShouldNotBe(Guid.Empty);
        entity.CreatedAt.ShouldNotBe(default);
        entity.IsDeleted.ShouldBeFalse();
        entity.DeletedAt.ShouldBeNull();
    }

    [Fact]
    public void MarkDeleted_Should_SoftDelete()
    {
        SampleEntity entity = SampleEntity.Create(Guid.CreateVersion7(), "foo", null);

        entity.MarkDeleted();

        entity.IsDeleted.ShouldBeTrue();
        entity.DeletedAt.ShouldNotBeNull();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Domain.UnitTests --filter "FullyQualifiedName~SampleEntityTests"`
Expected: FAIL — compile error, `SampleEntity.Create` and `MarkDeleted` do not exist.

- [ ] **Step 3: Harden `Entity`**

Replace `src/SharedKernel/Entity.cs`:

```csharp
namespace SharedKernel;

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

- [ ] **Step 4: Give `SampleEntity` a factory**

Replace `src/Domain/SampleEntities/SampleEntity.cs`:

```csharp
using SharedKernel;

namespace Domain.SampleEntities;

public sealed class SampleEntity : Entity
{
    private SampleEntity()
    {
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public static SampleEntity Create(Guid tenantId, string name, string? description)
    {
        var entity = new SampleEntity
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            Name = name,
            Description = description,
        };

        entity.MarkCreated();

        return entity;
    }
}
```

- [ ] **Step 5: Update the create handler to use the factory**

In `src/Application/SampleEntities/Create/CreateSampleEntityCommandHandler.cs`, replace the object initializer with the factory call. The method body becomes:

```csharp
    public async Task<Result<Guid>> Handle(
        CreateSampleEntityCommand command,
        CancellationToken cancellationToken)
    {
        Guid tenantId = userContext.TenantId
            ?? throw new InvalidOperationException("TenantId is required to create a SampleEntity.");

        SampleEntity entity = SampleEntity.Create(tenantId, command.Name, command.Description);

        dbContext.SampleEntities.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);

        return entity.Id;
    }
```

- [ ] **Step 6: Remove the duplicated soft-delete from `Role` and `Group`**

In `src/Auth.Domain/Roles/Role.cs`, delete this method entirely:

```csharp
    public void Delete()
    {
        IsDeleted = true;
        DeletedAt = DateTimeOffset.UtcNow;
    }
```

Do the same in `src/Auth.Domain/Groups/Group.cs`.

Then change `.Delete()` to `.MarkDeleted()` at all four call sites — two handlers and two
tests. They are enumerated so none is missed:

- `src/Auth.Application/Admin/Roles/Delete/DeleteRoleCommandHandler.cs:25` — `role.Delete();`
- `src/Auth.Application/Admin/Groups/Delete/DeleteGroupCommandHandler.cs:25` — `group.Delete();`
- `tests/Auth.Domain.UnitTests/Roles/RoleTests.cs:49` — `role.Delete();`
- `tests/Auth.Domain.UnitTests/Groups/GroupTests.cs:37` — `group.Delete();`

Confirm the list is exhausted:

```bash
grep -rn "\.Delete()" src tests --include="*.cs" | grep -v "/obj/"
```

Expected: no output.

- [ ] **Step 7: Fix the query-handler tests that build entities by object initializer**

`tests/Application.UnitTests/SampleEntities/GetSampleEntityByIdQueryHandlerTests.cs` builds
`SampleEntity` with object initializers, including `IsDeleted = true`. Both are now impossible.
Replace the three `var entity = new SampleEntity { ... };` blocks as follows.

In `Handle_Should_ReturnEntity_WhenFound`:

```csharp
        SampleEntity entity = SampleEntity.Create(tenantId, "Test", "d");
```

In `Handle_Should_ReturnNotFound_WhenEntityIsSoftDeleted`:

```csharp
        SampleEntity entity = SampleEntity.Create(tenantId, "Test", null);
        entity.MarkDeleted();
```

In `Handle_Should_ReturnNotFound_WhenEntityBelongsToAnotherTenant`:

```csharp
        SampleEntity entity = SampleEntity.Create(Guid.CreateVersion7(), "Test", null);
```

`CreateSampleEntityCommandHandlerTests` only reads properties, so it still compiles here. It
changes in Task 4.

- [ ] **Step 8: Run the full suite**

Run: `dotnet build BaseProjectScaffold.sln && dotnet test BaseProjectScaffold.sln`
Expected: build succeeds, all tests pass.

- [ ] **Step 9: Commit**

```bash
git add -A
git commit -m "refactor(domain): remove public setters from Entity, add SampleEntity factory"
```

---

### Task 4: `SampleEntity` value objects, end to end

The canonical example. Schema and HTTP contract must both come out unchanged.

**Files:**
- Create: `src/SharedKernel/TenantId.cs`
- Create: `src/Domain/SampleEntities/SampleEntityId.cs`
- Create: `src/Domain/SampleEntities/SampleEntityName.cs`
- Modify: `src/Domain/SampleEntities/SampleEntity.cs`
- Modify: `src/Domain/SampleEntities/SampleEntityErrors.cs`
- Modify: `src/Infra/Config/SampleEntityConfiguration.cs`
- Modify: `src/Application/SampleEntities/Create/CreateSampleEntityCommandHandler.cs`
- Modify: `src/Application/SampleEntities/GetById/GetSampleEntityByIdQueryHandler.cs`
- Modify: `tests/Domain.UnitTests/SampleEntities/SampleEntityTests.cs`
- Create: `tests/Domain.UnitTests/SampleEntities/SampleEntityNameTests.cs`
- Modify: `tests/Application.UnitTests/SampleEntities/CreateSampleEntityCommandHandlerTests.cs`
- Modify: `tests/Application.UnitTests/SampleEntities/GetSampleEntityByIdQueryHandlerTests.cs`

**Interfaces:**
- Consumes: `SharedKernel.IStronglyTypedId<TSelf, TValue>` (Task 2); `Entity.MarkCreated()` (Task 3).
- Produces:
  - `SharedKernel.TenantId` — `readonly record struct TenantId(Guid Value)`, `TenantId.From(Guid)`, `TenantId.New()`.
  - `Domain.SampleEntities.SampleEntityId` — same shape, plus `IParsable<SampleEntityId>`.
  - `Domain.SampleEntities.SampleEntityName` — `static Result<SampleEntityName> Create(string? input)`, `const int MaxLength = 200`, `string Value`.
  - `SampleEntity.Create(TenantId tenantId, SampleEntityName name, string? description)` returning `SampleEntity`.
  - `SampleEntityErrors.NameRequired`, `SampleEntityErrors.NameTooLong`.

- [ ] **Step 1: Write the failing value-object test**

Create `tests/Domain.UnitTests/SampleEntities/SampleEntityNameTests.cs`:

```csharp
using Domain.SampleEntities;
using Shouldly;

namespace Domain.UnitTests.SampleEntities;

public class SampleEntityNameTests
{
    [Fact]
    public void Create_Should_Succeed_WhenNameIsValid()
    {
        var result = SampleEntityName.Create("  Widget  ");

        result.IsSuccess.ShouldBeTrue();
        result.Value.Value.ShouldBe("Widget");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_Should_Fail_WhenNameIsBlank(string? input)
    {
        var result = SampleEntityName.Create(input);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("SampleEntity.NameRequired");
    }

    [Fact]
    public void Create_Should_Fail_WhenNameExceedsMaxLength()
    {
        var result = SampleEntityName.Create(new string('a', SampleEntityName.MaxLength + 1));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("SampleEntity.NameTooLong");
    }

    [Fact]
    public void Equality_Should_BeStructural()
    {
        SampleEntityName a = SampleEntityName.Create("same").Value;
        SampleEntityName b = SampleEntityName.Create("same").Value;

        a.ShouldBe(b);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Domain.UnitTests --filter "FullyQualifiedName~SampleEntityNameTests"`
Expected: FAIL — compile error, `SampleEntityName` does not exist.

- [ ] **Step 3: Write `TenantId` in SharedKernel**

Create `src/SharedKernel/TenantId.cs`. It lives here because `Domain` and `Auth.Domain` both need it and neither may reference the other.

```csharp
namespace SharedKernel;

public readonly record struct TenantId(Guid Value)
    : IStronglyTypedId<TenantId, Guid>, IParsable<TenantId>
{
    public static TenantId New() => new(Guid.CreateVersion7());

    public static TenantId From(Guid value) => new(value);

    public override string ToString() => Value.ToString();

    public static TenantId Parse(string s, IFormatProvider? provider) => new(Guid.Parse(s));

    public static bool TryParse(string? s, IFormatProvider? provider, out TenantId result)
    {
        if (Guid.TryParse(s, out Guid value))
        {
            result = new TenantId(value);
            return true;
        }

        result = default;
        return false;
    }
}
```

- [ ] **Step 4: Write `SampleEntityId`**

Create `src/Domain/SampleEntities/SampleEntityId.cs`:

```csharp
using SharedKernel;

namespace Domain.SampleEntities;

public readonly record struct SampleEntityId(Guid Value)
    : IStronglyTypedId<SampleEntityId, Guid>, IParsable<SampleEntityId>
{
    public static SampleEntityId New() => new(Guid.CreateVersion7());

    public static SampleEntityId From(Guid value) => new(value);

    public override string ToString() => Value.ToString();

    public static SampleEntityId Parse(string s, IFormatProvider? provider) => new(Guid.Parse(s));

    public static bool TryParse(string? s, IFormatProvider? provider, out SampleEntityId result)
    {
        if (Guid.TryParse(s, out Guid value))
        {
            result = new SampleEntityId(value);
            return true;
        }

        result = default;
        return false;
    }
}
```

- [ ] **Step 5: Write `SampleEntityName` and its errors**

Create `src/Domain/SampleEntities/SampleEntityName.cs`:

```csharp
using SharedKernel;

namespace Domain.SampleEntities;

public sealed record SampleEntityName
{
    public const int MaxLength = 200;

    private SampleEntityName(string value) => Value = value;

    public string Value { get; private init; }

    public static Result<SampleEntityName> Create(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return Result.Failure<SampleEntityName>(SampleEntityErrors.NameRequired);
        }

        string trimmed = input.Trim();

        return trimmed.Length > MaxLength
            ? Result.Failure<SampleEntityName>(SampleEntityErrors.NameTooLong)
            : new SampleEntityName(trimmed);
    }

    public override string ToString() => Value;
}
```

Replace `src/Domain/SampleEntities/SampleEntityErrors.cs`:

```csharp
using SharedKernel;

namespace Domain.SampleEntities;

public static class SampleEntityErrors
{
    public static Error NotFound(SampleEntityId id) =>
        Error.NotFound("SampleEntity.NotFound", $"Sample entity with id '{id}' was not found.");

    public static readonly Error NameRequired =
        Error.Validation("SampleEntity.NameRequired", "Name is required.");

    public static readonly Error NameTooLong =
        Error.Validation(
            "SampleEntity.NameTooLong",
            $"Name must be at most {SampleEntityName.MaxLength} characters.");
}
```

- [ ] **Step 6: Run the value-object test to verify it passes**

Run: `dotnet test tests/Domain.UnitTests --filter "FullyQualifiedName~SampleEntityNameTests"`
Expected: PASS, 6 tests.

- [ ] **Step 7: Convert the aggregate**

Replace `src/Domain/SampleEntities/SampleEntity.cs`:

```csharp
using SharedKernel;

namespace Domain.SampleEntities;

public sealed class SampleEntity : Entity
{
    private SampleEntity()
    {
    }

    public SampleEntityId Id { get; private set; }

    public TenantId TenantId { get; private set; }

    public SampleEntityName Name { get; private set; } = null!;

    public string? Description { get; private set; }

    public static SampleEntity Create(TenantId tenantId, SampleEntityName name, string? description)
    {
        var entity = new SampleEntity
        {
            Id = SampleEntityId.New(),
            TenantId = tenantId,
            Name = name,
            Description = description,
        };

        entity.MarkCreated();

        return entity;
    }

    public void Rename(SampleEntityName name) => Name = name;
}
```

Update `tests/Domain.UnitTests/SampleEntities/SampleEntityTests.cs` for the new signature:

```csharp
using Domain.SampleEntities;
using SharedKernel;
using Shouldly;

namespace Domain.UnitTests.SampleEntities;

public class SampleEntityTests
{
    private static SampleEntityName Name(string value) => SampleEntityName.Create(value).Value;

    [Fact]
    public void Create_Should_SetCreatedAt_AndNotBeDeleted()
    {
        var tenantId = TenantId.New();

        SampleEntity entity = SampleEntity.Create(tenantId, Name("foo"), "bar");

        entity.Name.Value.ShouldBe("foo");
        entity.Description.ShouldBe("bar");
        entity.TenantId.ShouldBe(tenantId);
        entity.Id.Value.ShouldNotBe(Guid.Empty);
        entity.CreatedAt.ShouldNotBe(default);
        entity.IsDeleted.ShouldBeFalse();
        entity.DeletedAt.ShouldBeNull();
    }

    [Fact]
    public void MarkDeleted_Should_SoftDelete()
    {
        SampleEntity entity = SampleEntity.Create(TenantId.New(), Name("foo"), null);

        entity.MarkDeleted();

        entity.IsDeleted.ShouldBeTrue();
        entity.DeletedAt.ShouldNotBeNull();
    }
}
```

- [ ] **Step 8: Update the EF configuration**

Replace `src/Infra/Config/SampleEntityConfiguration.cs`. Both `HasColumnName` calls are load-bearing — without them snake_case produces `name_value`.

```csharp
using Domain.SampleEntities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SharedKernel;

namespace Infra.Config;

internal sealed class SampleEntityConfiguration : AbstractConfiguration<SampleEntity>
{
    public override void Configure(EntityTypeBuilder<SampleEntity> builder)
    {
        base.Configure(builder);

        builder.ToTable("sample_entities");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => SampleEntityId.From(value));

        builder.Property(e => e.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(id => id.Value, value => TenantId.From(value))
            .IsRequired();

        builder.HasIndex(e => new { e.TenantId, e.Id });

        builder.ComplexProperty(e => e.Name, b => b
            .Property(n => n.Value)
            .HasColumnName("name")
            .HasMaxLength(SampleEntityName.MaxLength)
            .IsRequired());

        builder.Property(e => e.Description).HasMaxLength(2000);
    }
}
```

- [ ] **Step 9: Update the handlers**

In `src/Application/SampleEntities/Create/CreateSampleEntityCommandHandler.cs`, the body becomes — note the value object failure is returned as a `Result`, never thrown:

```csharp
    public async Task<Result<Guid>> Handle(
        CreateSampleEntityCommand command,
        CancellationToken cancellationToken)
    {
        Guid rawTenantId = userContext.TenantId
            ?? throw new InvalidOperationException("TenantId is required to create a SampleEntity.");

        Result<SampleEntityName> name = SampleEntityName.Create(command.Name);

        if (name.IsFailure)
        {
            return Result.Failure<Guid>(name.Error);
        }

        SampleEntity entity = SampleEntity.Create(
            TenantId.From(rawTenantId),
            name.Value,
            command.Description);

        dbContext.SampleEntities.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);

        return entity.Id.Value;
    }
```

Add `using SharedKernel;` if it is not already imported.

In `src/Application/SampleEntities/GetById/GetSampleEntityByIdQueryHandler.cs`, the body becomes. The two-step shape is deliberate: `e.Name.Value` translates because `Name` is a complex property, while `e.Id` is selected whole because a `ValueConverter` is opaque to member access.

```csharp
    public async Task<Result<SampleEntityResponse>> Handle(
        GetSampleEntityByIdQuery query,
        CancellationToken cancellationToken)
    {
        Guid? rawTenantId = userContext.TenantId;
        var id = SampleEntityId.From(query.Id);

        var row = await dbContext.SampleEntities
            .Where(e => e.Id == id
                && !e.IsDeleted
                && (rawTenantId == null || e.TenantId == TenantId.From(rawTenantId.Value)))
            .Select(e => new { e.Id, Name = e.Name.Value, e.Description })
            .FirstOrDefaultAsync(cancellationToken);

        return row is null
            ? Result.Failure<SampleEntityResponse>(SampleEntityErrors.NotFound(id))
            : new SampleEntityResponse(row.Id.Value, row.Name, row.Description);
    }
```

Add `using SharedKernel;` if it is not already imported.

- [ ] **Step 10: Update the application unit tests for the new types**

In `tests/Application.UnitTests/SampleEntities/CreateSampleEntityCommandHandlerTests.cs`, the
three assertions in `Handle_Should_PersistEntity_AndReturnId` now compare wrapped values. Add
`using SharedKernel;` and replace:

```csharp
        sampleEntities[0].Name.Value.ShouldBe("Valid Name");
        sampleEntities[0].TenantId.ShouldBe(TenantId.From(tenantId));
        sampleEntities[0].Id.Value.ShouldBe(result.Value);
```

Then add a test proving the value object rejects bad input through `Result`, not an exception:

```csharp
    [Fact]
    public async Task Handle_Should_ReturnValidationFailure_WhenNameIsBlank()
    {
        var dbContext = new Mock<IApplicationDbContext>();
        var userContext = new Mock<IUserContext>();
        userContext.SetupGet(u => u.TenantId).Returns(Guid.CreateVersion7());

        var handler = new CreateSampleEntityCommandHandler(dbContext.Object, userContext.Object);

        var result = await handler.Handle(
            new CreateSampleEntityCommand("   ", "desc"),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("SampleEntity.NameRequired");
        dbContext.Verify(c => c.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
```

In `tests/Application.UnitTests/SampleEntities/GetSampleEntityByIdQueryHandlerTests.cs`, add
`using SharedKernel;` and update the three construction sites from Task 3 Step 7 plus the two
places that read `entity.Id`:

```csharp
        SampleEntity entity = SampleEntity.Create(
            TenantId.From(tenantId), SampleEntityName.Create("Test").Value, "d");
```

```csharp
        SampleEntity entity = SampleEntity.Create(
            TenantId.From(tenantId), SampleEntityName.Create("Test").Value, null);
        entity.MarkDeleted();
```

```csharp
        SampleEntity entity = SampleEntity.Create(
            TenantId.New(), SampleEntityName.Create("Test").Value, null);
```

Every `new GetSampleEntityByIdQuery(entity.Id)` becomes `new GetSampleEntityByIdQuery(entity.Id.Value)`,
and `result.Value.Id.ShouldBe(entity.Id)` becomes `result.Value.Id.ShouldBe(entity.Id.Value)`.

- [ ] **Step 11: Run the full suite**

Run: `dotnet build BaseProjectScaffold.sln && dotnet test BaseProjectScaffold.sln`
Expected: build succeeds, all tests pass.

`tests/Web.API.IntegrationTests/Endpoints/SampleEntityEndpointTests.cs` must pass **without any
edit** — it talks HTTP and reads `body.Id` as a bare `Guid`. If it needs changing, the contract
moved and Task 4 has a bug.

If `GetSampleEntityByIdQueryHandlerTests` fails with an EF InMemory translation error on `TenantId.From(rawTenantId.Value)`, hoist it out of the expression tree into a local before the query:

```csharp
        TenantId? tenantId = rawTenantId is null ? null : TenantId.From(rawTenantId.Value);
```

and filter with `(tenantId == null || e.TenantId == tenantId)`.

- [ ] **Step 12: Verify the schema did not move**

```bash
dotnet ef migrations add VerifyNoSchemaChange \
  --project src/Infra --startup-project src/EntryPoints/Web.API \
  --output-dir Database/Migrations
```

Open the generated migration. `Up` and `Down` **must both be empty**. Then remove it:

```bash
dotnet ef migrations remove --project src/Infra --startup-project src/EntryPoints/Web.API
```

If the migration is not empty, the mapping is wrong — most likely a missing `HasColumnName` on the complex property. Fix and repeat before committing.

- [ ] **Step 13: Commit**

```bash
git add -A
git commit -m "feat(domain): SampleEntity value objects with unchanged schema and contract"
```

---

### Task 5: Tenant-guard characterization test

The guard works today. This test proves it, and is the alarm that fires when the typed-id conversion silences it in a later plan. It passes on arrival.

**Files:**
- Create: `tests/Auth.API.IntegrationTests/Database/AuthDbContextTenantGuardTests.cs`

**Interfaces:**
- Consumes: `Auth.Infra.Database.AuthDbContext`, `Auth.Application.Abstractions.Tenancy.ITenantContext`, `Auth.Domain.Tenants.Tenant`.
- Produces: nothing consumed by later tasks. Pure safety net.

- [ ] **Step 1: Write the test**

Create `tests/Auth.API.IntegrationTests/Database/AuthDbContextTenantGuardTests.cs`. EF InMemory is enough — the guard runs in `SaveChangesAsync` before the provider is involved, so no container is needed.

```csharp
using Auth.Application.Abstractions.Tenancy;
using Auth.Domain.Roles;
using Auth.Infra.Database;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Auth.API.IntegrationTests.Database;

/// <summary>
/// Characterization test for <c>AuthDbContext.EnforceTenantGuard</c>. It passes today. If a
/// future refactor changes the CLR type of TenantId without updating the guard's type check,
/// the guard degrades to a silent no-op and THIS TEST IS THE ONLY THING THAT GOES RED.
/// Do not delete or weaken it.
/// </summary>
public class AuthDbContextTenantGuardTests
{
    private sealed class FixedTenantContext(Guid tenantId) : ITenantContext
    {
        public Guid TenantId { get; } = tenantId;

        public bool HasTenant => true;
    }

    private static AuthDbContext CreateContext(Guid ambientTenantId)
    {
        var options = new DbContextOptionsBuilder<AuthDbContext>()
            .UseInMemoryDatabase($"tenant-guard-{Guid.CreateVersion7()}")
            .Options;

        return new AuthDbContext(options, new FixedTenantContext(ambientTenantId));
    }

    [Fact]
    public async Task SaveChanges_Should_Throw_WhenEntityBelongsToAnotherTenant()
    {
        Guid ambientTenant = Guid.CreateVersion7();
        Guid foreignTenant = Guid.CreateVersion7();

        await using AuthDbContext context = CreateContext(ambientTenant);

        Role role = Role.Create(foreignTenant, "auditor", "Read-only access");
        context.Roles.Add(role);

        var exception = await Should.ThrowAsync<InvalidOperationException>(
            () => context.SaveChangesAsync(TestContext.Current.CancellationToken));

        exception.Message.ShouldContain("Tenant guard violation");
    }

    [Fact]
    public async Task SaveChanges_Should_Succeed_WhenEntityBelongsToTheAmbientTenant()
    {
        Guid ambientTenant = Guid.CreateVersion7();

        await using AuthDbContext context = CreateContext(ambientTenant);

        Role role = Role.Create(ambientTenant, "auditor", "Read-only access");
        context.Roles.Add(role);

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        context.Roles.Count().ShouldBe(1);
    }
}
```

- [ ] **Step 2: Run the test — it must PASS**

Run: `dotnet test tests/Auth.API.IntegrationTests --filter "FullyQualifiedName~AuthDbContextTenantGuardTests"`
Expected: PASS, 2 tests.

This is a characterization test, not a red-first TDD test — it locks in behaviour that already works. If it fails now, the guard is already broken and that is a separate bug to report before continuing.

If `TestContext.Current.CancellationToken` does not resolve, the xunit version in this project predates it — use `CancellationToken.None` instead.

- [ ] **Step 3: Prove the test actually guards**

Temporarily break the guard to confirm the alarm works. In `src/Auth.Infra/Database/AuthDbContext.cs`, change

```csharp
            if (current is not Guid currentTenantId)
```

to

```csharp
            if (current is not string currentTenantId)
```

Run the test again. Expected: the first test **FAILS** — this simulates exactly what a naive typed-id conversion would do.

Then revert the edit:

```bash
git checkout -- src/Auth.Infra/Database/AuthDbContext.cs
```

Re-run the test and confirm it passes again.

- [ ] **Step 4: Commit**

```bash
git add tests/Auth.API.IntegrationTests/Database
git commit -m "test(auth): characterize tenant guard before typed-id conversion"
```

---

## Verification for the whole plan

- [ ] `dotnet build BaseProjectScaffold.sln` — no warnings introduced by these changes.
- [ ] `dotnet test BaseProjectScaffold.sln` — every suite green.
- [ ] `dotnet ef migrations add Check` on **both** contexts produces empty `Up`/`Down`; remove afterwards.
- [ ] `dotnet format BaseProjectScaffold.sln --verify-no-changes`.
- [ ] Manual: `POST /api/v1/sample-entities` and `GET /api/v1/sample-entities/{id}` return the same JSON shape as before — id as a bare GUID string, no `{"value": ...}` wrapper anywhere.

## What this plan deliberately leaves out

- The seven `Auth.Domain` aggregates (spec phases 4–8) — next plan, once this one proves the pattern.
- The typed tenant guard and `ReconcileAsync<TJoin, TId>` (phase 9) — they depend on Auth aggregates being converted.
- Architecture rules and CLAUDE.md updates (phase 10) — written last so they describe the finished state.
- Registering `StronglyTypedIdJsonConverterFactory` into the Web.API and Blazor `JsonSerializerOptions`. Nothing serializes a typed id yet: `SampleEntityResponse` still carries a bare `Guid`. It gets wired when the first typed id reaches a response DTO.

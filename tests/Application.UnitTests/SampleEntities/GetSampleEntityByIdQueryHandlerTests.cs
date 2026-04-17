using Application.Abstractions.Data;
using Application.SampleEntities.GetById;
using Domain.SampleEntities;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Application.UnitTests.SampleEntities;

public class GetSampleEntityByIdQueryHandlerTests
{
    private sealed class TestDbContext(DbContextOptions<TestDbContext> options)
        : DbContext(options), IApplicationDbContext
    {
        public DbSet<SampleEntity> SampleEntities => Set<SampleEntity>();
    }

    private static TestDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase($"sample-{Guid.NewGuid()}")
            .Options);

    [Fact]
    public async Task Handle_Should_ReturnNotFound_WhenEntityDoesNotExist()
    {
        await using var ctx = CreateContext();
        var handler = new GetSampleEntityByIdQueryHandler(ctx);

        var result = await handler.Handle(
            new GetSampleEntityByIdQuery(Guid.NewGuid()),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("SampleEntity.NotFound");
    }

    [Fact]
    public async Task Handle_Should_ReturnEntity_WhenFound()
    {
        await using var ctx = CreateContext();
        var entity = new SampleEntity
        {
            Id = Guid.NewGuid(),
            Name = "Test",
            Description = "d",
            CreatedAt = DateTimeOffset.UtcNow
        };
        ctx.SampleEntities.Add(entity);
        await ctx.SaveChangesAsync();

        var handler = new GetSampleEntityByIdQueryHandler(ctx);
        var result = await handler.Handle(new GetSampleEntityByIdQuery(entity.Id), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Id.ShouldBe(entity.Id);
        result.Value.Name.ShouldBe("Test");
    }

    [Fact]
    public async Task Handle_Should_ReturnNotFound_WhenEntityIsSoftDeleted()
    {
        await using var ctx = CreateContext();
        var entity = new SampleEntity
        {
            Id = Guid.NewGuid(),
            Name = "Test",
            CreatedAt = DateTimeOffset.UtcNow,
            IsDeleted = true,
            DeletedAt = DateTimeOffset.UtcNow
        };
        ctx.SampleEntities.Add(entity);
        await ctx.SaveChangesAsync();

        var handler = new GetSampleEntityByIdQueryHandler(ctx);
        var result = await handler.Handle(new GetSampleEntityByIdQuery(entity.Id), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
    }
}

using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.SampleEntities.Create;
using Domain.SampleEntities;
using Microsoft.EntityFrameworkCore;
using Moq;
using Shouldly;

namespace Application.UnitTests.SampleEntities;

public class CreateSampleEntityCommandHandlerTests
{
    [Fact]
    public async Task Handle_Should_PersistEntity_AndReturnId()
    {
        var sampleEntities = new List<SampleEntity>();
        var mockSet = new Mock<DbSet<SampleEntity>>();
        mockSet
            .Setup(s => s.Add(It.IsAny<SampleEntity>()))
            .Callback<SampleEntity>(sampleEntities.Add);

        var dbContext = new Mock<IApplicationDbContext>();
        dbContext.SetupGet(c => c.SampleEntities).Returns(mockSet.Object);
        dbContext
            .Setup(c => c.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        Guid tenantId = Guid.NewGuid();
        var userContext = new Mock<IUserContext>();
        userContext.SetupGet(u => u.TenantId).Returns(tenantId);

        var handler = new CreateSampleEntityCommandHandler(dbContext.Object, userContext.Object);

        var result = await handler.Handle(
            new CreateSampleEntityCommand("Valid Name", "desc"),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        sampleEntities.ShouldHaveSingleItem();
        sampleEntities[0].Name.ShouldBe("Valid Name");
        sampleEntities[0].TenantId.ShouldBe(tenantId);
        sampleEntities[0].Id.ShouldBe(result.Value);
        dbContext.Verify(c => c.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_Should_Throw_WhenTenantIdMissing()
    {
        var dbContext = new Mock<IApplicationDbContext>();
        var userContext = new Mock<IUserContext>();
        userContext.SetupGet(u => u.TenantId).Returns((Guid?)null);

        var handler = new CreateSampleEntityCommandHandler(dbContext.Object, userContext.Object);

        await Should.ThrowAsync<InvalidOperationException>(() => handler.Handle(
            new CreateSampleEntityCommand("Valid Name", "desc"),
            CancellationToken.None));
    }
}

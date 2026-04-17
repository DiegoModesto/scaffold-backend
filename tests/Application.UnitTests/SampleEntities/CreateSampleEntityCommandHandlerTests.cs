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

        var handler = new CreateSampleEntityCommandHandler(dbContext.Object);

        var result = await handler.Handle(
            new CreateSampleEntityCommand("Valid Name", "desc"),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        sampleEntities.ShouldHaveSingleItem();
        sampleEntities[0].Name.ShouldBe("Valid Name");
        sampleEntities[0].Id.ShouldBe(result.Value);
        dbContext.Verify(c => c.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}

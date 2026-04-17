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
    public async Task Handle_Should_ReturnValidation_WhenNameIsEmpty()
    {
        var dbContext = new Mock<IApplicationDbContext>();
        var handler = new CreateSampleEntityCommandHandler(dbContext.Object);

        var result = await handler.Handle(new CreateSampleEntityCommand(string.Empty, null), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(SampleEntityErrors.NameRequired.Code);
    }
}

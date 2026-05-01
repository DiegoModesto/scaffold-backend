using Application.Abstractions.Messaging;
using Application.SampleEntities.Events;
using Application.SampleEntities.Publish;
using Moq;
using Shouldly;

namespace Application.UnitTests.SampleEntities;

public class PublishSampleEventCommandHandlerTests
{
    [Fact]
    public async Task Handle_Should_PublishEventWithRoutingKey_AndReturnGeneratedId()
    {
        var publisher = new Mock<IMessagePublisher>();
        SampleEntityCreatedEvent? captured = null;
        string? capturedRoutingKey = null;

        publisher
            .Setup(p => p.PublishAsync(
                It.IsAny<SampleEntityCreatedEvent>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback<SampleEntityCreatedEvent, string, CancellationToken>((evt, key, _) =>
            {
                captured = evt;
                capturedRoutingKey = key;
            })
            .Returns(Task.CompletedTask);

        var handler = new PublishSampleEventCommandHandler(publisher.Object);

        var result = await handler.Handle(
            new PublishSampleEventCommand("polled", "desc"),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldNotBe(Guid.Empty);

        captured.ShouldNotBeNull();
        captured!.Id.ShouldBe(result.Value);
        captured.Name.ShouldBe("polled");
        captured.Description.ShouldBe("desc");

        capturedRoutingKey.ShouldBe(PublishSampleEventCommandHandler.RoutingKey);

        publisher.Verify(p => p.PublishAsync(
            It.IsAny<SampleEntityCreatedEvent>(),
            PublishSampleEventCommandHandler.RoutingKey,
            It.IsAny<CancellationToken>()), Times.Once);
    }
}

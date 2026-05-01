using System.Collections.Concurrent;
using Application.Abstractions.Messaging;

namespace Web.API.IntegrationTests.Infrastructure;

public sealed class FakeMessagePublisher : IMessagePublisher
{
    private readonly ConcurrentBag<(object Message, string RoutingKey)> _published = [];

    public IReadOnlyCollection<(object Message, string RoutingKey)> Published => _published;

    public Task PublishAsync<TMessage>(TMessage message, string routingKey, CancellationToken cancellationToken = default)
        where TMessage : class
    {
        _published.Add((message, routingKey));
        return Task.CompletedTask;
    }
}

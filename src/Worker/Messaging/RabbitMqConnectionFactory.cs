using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Worker.Messaging;

public sealed class RabbitMqConnectionFactory(IOptions<RabbitMqOptions> options)
{
    private readonly RabbitMqOptions _options = options.Value;

    public Task<IConnection> CreateConnectionAsync(CancellationToken cancellationToken = default)
    {
        var factory = new ConnectionFactory
        {
            HostName = _options.Host,
            Port = _options.Port,
            UserName = _options.User,
            Password = _options.Password,
            VirtualHost = _options.VirtualHost
        };

        return factory.CreateConnectionAsync(cancellationToken);
    }
}

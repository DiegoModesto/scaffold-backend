namespace Worker.Messaging;

public sealed class RabbitMqOptions
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string User { get; set; } = "guest";
    public string Password { get; set; } = "guest";
    public string VirtualHost { get; set; } = "/";
    public string QueueName { get; set; } = "sample.queue";
    public string ExchangeName { get; set; } = "sample.exchange";
    public string RoutingKey { get; set; } = "sample.key";
}

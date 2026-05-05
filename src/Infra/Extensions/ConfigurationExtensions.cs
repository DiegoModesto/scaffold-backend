using Microsoft.Extensions.Configuration;

namespace Infra.Extensions;

public static class ConfigurationExtensions
{
    private static readonly IReadOnlyDictionary<string, string> EnvMappings = new Dictionary<string, string>
    {
        { "DB_CONNECTION_STRING", "ConnectionStrings:Database" },
        { "JWT_SECRET", "Jwt:Secret" },
        { "JWT_ISSUER", "Jwt:Issuer" },
        { "JWT_AUDIENCE", "Jwt:Audience" },
        { "JWT_EXPIRATION_MINUTES", "Jwt:ExpirationInMinutes" },
        { "RABBITMQ_HOST", "RabbitMq:Host" },
        { "RABBITMQ_PORT", "RabbitMq:Port" },
        { "RABBITMQ_USER", "RabbitMq:User" },
        { "RABBITMQ_PASSWORD", "RabbitMq:Password" },
        { "RABBITMQ_VIRTUALHOST", "RabbitMq:VirtualHost" },
        { "RABBITMQ_EXCHANGE", "RabbitMq:ExchangeName" },
        { "RABBITMQ_QUEUE", "RabbitMq:QueueName" },
        { "RABBITMQ_ROUTINGKEY", "RabbitMq:RoutingKey" }
    };

    public static void MapEnvironmentVariables(this IConfiguration configuration)
    {
        foreach (KeyValuePair<string, string> mapping in EnvMappings)
        {
            string? envValue = Environment.GetEnvironmentVariable(mapping.Key);

            if (!string.IsNullOrWhiteSpace(envValue))
            {
                configuration[mapping.Value] = envValue;
            }
        }
    }
}

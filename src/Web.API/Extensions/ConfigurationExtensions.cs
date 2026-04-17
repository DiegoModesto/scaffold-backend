namespace Web.API.Extensions;

public static class ConfigurationExtensions
{
    public static void MapEnvironmentVariables(this IConfiguration configuration)
    {
        var envMappings = new Dictionary<string, string>
        {
            { "DB_CONNECTION_STRING", "ConnectionStrings:Database" },
            { "JWT_SECRET", "Jwt:Secret" },
            { "JWT_ISSUER", "Jwt:Issuer" },
            { "JWT_AUDIENCE", "Jwt:Audience" },
            { "JWT_EXPIRATION_MINUTES", "Jwt:ExpirationInMinutes" },
            { "RABBITMQ_HOST", "RabbitMq:Host" },
            { "RABBITMQ_USER", "RabbitMq:User" },
            { "RABBITMQ_PASSWORD", "RabbitMq:Password" }
        };

        foreach (KeyValuePair<string, string> mapping in envMappings)
        {
            string? envValue = Environment.GetEnvironmentVariable(mapping.Key);

            if (!string.IsNullOrWhiteSpace(envValue))
            {
                configuration[mapping.Value] = envValue;
            }
        }
    }
}

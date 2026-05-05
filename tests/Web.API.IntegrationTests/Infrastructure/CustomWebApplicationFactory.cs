using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Infra.Database;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace Web.API.IntegrationTests.Infrastructure;

public sealed class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string TestJwtSecret = "integration-test-secret-that-is-long-enough-32bytes!";
    public const string TestIssuer = "base-scaffold";
    public const string TestAudience = "base-scaffold-clients";

    private readonly string _dbName = $"IntegrationTestDb_{Guid.NewGuid()}";

    public FakeMessagePublisher MessagePublisher { get; } = new();

    public CustomWebApplicationFactory()
    {
        Environment.SetEnvironmentVariable(
            "DB_CONNECTION_STRING",
            "Host=localhost;Port=5432;Database=test;Username=test;Password=test;");
        Environment.SetEnvironmentVariable("JWT_SECRET", TestJwtSecret);
        Environment.SetEnvironmentVariable("JWT_ISSUER", TestIssuer);
        Environment.SetEnvironmentVariable("JWT_AUDIENCE", TestAudience);
        Environment.SetEnvironmentVariable("RABBITMQ_HOST", "localhost");
        Environment.SetEnvironmentVariable("RABBITMQ_USER", "test");
        Environment.SetEnvironmentVariable("RABBITMQ_PASSWORD", "test");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Secret"] = TestJwtSecret,
                ["Jwt:Issuer"] = TestIssuer,
                ["Jwt:Audience"] = TestAudience,
                ["Jwt:ExpirationInMinutes"] = "60",
                ["Jwt:RequireHttpsMetadata"] = "false",
                ["ConnectionStrings:Database"] =
                    "Host=localhost;Port=5432;Database=test;Username=test;Password=test;",
                ["RabbitMq:Host"] = "localhost",
                ["RabbitMq:User"] = "test",
                ["RabbitMq:Password"] = "test",
                ["RabbitMq:ExchangeName"] = "test.exchange",
                ["RabbitMq:QueueName"] = "test.queue",
                ["RabbitMq:RoutingKey"] = "test.key"
            });
        });

        builder.ConfigureServices(services =>
        {
            List<ServiceDescriptor> efDescriptors = services
                .Where(d => d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>)
                            || d.ServiceType == typeof(ApplicationDbContext)
                            || (d.ServiceType.FullName?.StartsWith("Microsoft.EntityFrameworkCore") ?? false))
                .ToList();

            foreach (ServiceDescriptor descriptor in efDescriptors)
            {
                services.Remove(descriptor);
            }

            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseInMemoryDatabase(_dbName));

            ServiceDescriptor? publisherDescriptor = services
                .FirstOrDefault(d => d.ServiceType == typeof(IMessagePublisher));
            if (publisherDescriptor is not null)
            {
                services.Remove(publisherDescriptor);
            }

            services.AddSingleton<IMessagePublisher>(MessagePublisher);
        });
    }

    public string CreateBearerToken(string userId = "integration-user")
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestJwtSecret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: TestIssuer,
            audience: TestAudience,
            claims:
            [
                new Claim(ClaimTypes.NameIdentifier, userId),
                new Claim(ClaimTypes.Name, userId)
            ],
            notBefore: DateTime.UtcNow,
            expires: DateTime.UtcNow.AddMinutes(30),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

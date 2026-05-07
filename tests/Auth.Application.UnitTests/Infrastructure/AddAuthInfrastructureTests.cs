using Auth.Infra;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Auth.Application.UnitTests.Infrastructure;

public sealed class AddAuthInfrastructureTests
{
    [Fact]
    public void AddAuthInfrastructure_Should_Throw_When_AuthDb_ConnectionString_Missing()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:AuthDb"] = "",
                ["Redis:ConnectionString"] = "localhost:6379",
            })
            .Build();

        IServiceCollection services = new ServiceCollection();

        Should.Throw<InvalidOperationException>(() =>
            services.AddAuthInfrastructure(configuration));
    }

    [Fact]
    public void AddAuthInfrastructure_Should_Throw_When_Redis_ConnectionString_Missing()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:AuthDb"] =
                    "Host=localhost;Port=5433;Database=auth_db;Username=postgres;Password=postgres",
                ["Redis:ConnectionString"] = "",
            })
            .Build();

        IServiceCollection services = new ServiceCollection();

        InvalidOperationException ex = Should.Throw<InvalidOperationException>(() =>
            services.AddAuthInfrastructure(configuration));

        ex.Message.ShouldContain("Redis:ConnectionString");
    }
}

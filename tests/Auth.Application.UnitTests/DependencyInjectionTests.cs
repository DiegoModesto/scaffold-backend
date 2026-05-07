using Auth.Application.Abstractions.Crypto;
using Auth.Application.Abstractions.Data;
using Auth.Application.Abstractions.Messaging;
using Auth.Application.Tenants.Resolve;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Shouldly;

namespace Auth.Application.UnitTests;

public sealed class DependencyInjectionTests
{
    [Fact]
    public void AddAuthApplication_RegistersHandlersAndDecorators()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Mock.Of<IAuthDbContext>());
        services.AddSingleton(Mock.Of<IClientSecretHasher>());
        services.AddAuthApplication();

        var sp = services.BuildServiceProvider();

        var handler = sp.GetService<IQueryHandler<ResolveTenantQuery, ResolveTenantResponse>>();
        handler.ShouldNotBeNull();
    }
}

using Application.Abstractions.Messaging;
using NetArchTest.Rules;
using Shouldly;

namespace Web.API.IntegrationTests.Architecture;

public class ArchitectureTests
{
    private static readonly System.Reflection.Assembly DomainAssembly =
        typeof(Domain.SampleEntities.SampleEntity).Assembly;

    private static readonly System.Reflection.Assembly ApplicationAssembly =
        typeof(global::Application.DependencyInjection).Assembly;

    private static readonly System.Reflection.Assembly InfraAssembly =
        typeof(Infra.DependencyInjection).Assembly;

    private static readonly System.Reflection.Assembly WebApiAssembly =
        typeof(global::Web.API.DependencyInjection).Assembly;

    [Fact]
    public void Domain_Should_Not_HaveDependencyOn_Application()
    {
        var result = Types.InAssembly(DomainAssembly)
            .ShouldNot().HaveDependencyOn("Application").GetResult();
        result.IsSuccessful.ShouldBeTrue();
    }

    [Fact]
    public void Domain_Should_Not_HaveDependencyOn_Infra()
    {
        var result = Types.InAssembly(DomainAssembly)
            .ShouldNot().HaveDependencyOn("Infra").GetResult();
        result.IsSuccessful.ShouldBeTrue();
    }

    [Fact]
    public void Domain_Should_Not_HaveDependencyOn_EntityFrameworkCore()
    {
        var result = Types.InAssembly(DomainAssembly)
            .ShouldNot().HaveDependencyOn("Microsoft.EntityFrameworkCore").GetResult();
        result.IsSuccessful.ShouldBeTrue();
    }

    [Fact]
    public void Application_Should_Not_HaveDependencyOn_Infra()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .ShouldNot().HaveDependencyOn("Infra").GetResult();
        result.IsSuccessful.ShouldBeTrue();
    }

    [Fact]
    public void Application_Should_Not_HaveDependencyOn_AspNetCore()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .ShouldNot().HaveDependencyOn("Microsoft.AspNetCore").GetResult();
        result.IsSuccessful.ShouldBeTrue();
    }

    [Fact]
    public void Infra_Should_Not_HaveDependencyOn_WebApi()
    {
        var result = Types.InAssembly(InfraAssembly)
            .ShouldNot().HaveDependencyOn("Web.API").GetResult();
        result.IsSuccessful.ShouldBeTrue();
    }

    [Fact]
    public void CommandHandlers_Should_BeSealed()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .That()
            .ImplementInterface(typeof(ICommandHandler<,>))
            .Or().ImplementInterface(typeof(ICommandHandler<>))
            .Should().BeSealed().GetResult();
        result.IsSuccessful.ShouldBeTrue();
    }

    [Fact]
    public void QueryHandlers_Should_BeSealed()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .That()
            .ImplementInterface(typeof(IQueryHandler<,>))
            .Should().BeSealed().GetResult();
        result.IsSuccessful.ShouldBeTrue();
    }

    [Fact]
    public void Endpoints_Should_BeSealed()
    {
        var result = Types.InAssembly(WebApiAssembly)
            .That().ImplementInterface(typeof(global::Web.API.Endpoints.IEndpoint))
            .Should().BeSealed().GetResult();
        result.IsSuccessful.ShouldBeTrue();
    }

    [Fact]
    public void MessagePublisher_Implementations_Should_LiveInInfraAssembly()
    {
        // The IMessagePublisher contract belongs to Application; concrete brokers (RabbitMQ etc.)
        // must live in Infra so other entrypoints can publish without taking a Worker dependency.
        var applicationImplementations = Types.InAssembly(ApplicationAssembly)
            .That().ImplementInterface(typeof(IMessagePublisher))
            .GetTypes();
        applicationImplementations.ShouldBeEmpty();

        var infraImplementations = Types.InAssembly(InfraAssembly)
            .That().ImplementInterface(typeof(IMessagePublisher))
            .GetTypes();
        infraImplementations.ShouldNotBeEmpty();
    }
}

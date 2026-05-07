using System.Reflection;
using NetArchTest.Rules;
using Shouldly;

namespace Gateway.IntegrationTests.Architecture;

public sealed class ArchitectureTests
{
    private static readonly Assembly GatewayAssembly = typeof(Gateway.Program).Assembly;

    [Fact]
    public void Gateway_ShouldNotDependOn_AuthDomain()
    {
        var result = Types.InAssembly(GatewayAssembly)
            .ShouldNot()
            .HaveDependencyOn("Auth.Domain")
            .GetResult();
        result.IsSuccessful.ShouldBeTrue($"Failing: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void Gateway_ShouldNotDependOn_AuthApplication()
    {
        var result = Types.InAssembly(GatewayAssembly)
            .ShouldNot()
            .HaveDependencyOn("Auth.Application")
            .GetResult();
        result.IsSuccessful.ShouldBeTrue($"Failing: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void Gateway_ShouldNotDependOn_AuthInfra()
    {
        var result = Types.InAssembly(GatewayAssembly)
            .ShouldNot()
            .HaveDependencyOn("Auth.Infra")
            .GetResult();
        result.IsSuccessful.ShouldBeTrue($"Failing: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void Gateway_ShouldNotDependOn_EntityFrameworkCore()
    {
        var result = Types.InAssembly(GatewayAssembly)
            .ShouldNot()
            .HaveDependencyOn("Microsoft.EntityFrameworkCore")
            .GetResult();
        result.IsSuccessful.ShouldBeTrue($"Failing: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }
}

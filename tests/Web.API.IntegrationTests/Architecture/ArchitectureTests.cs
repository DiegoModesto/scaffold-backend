using NetArchTest.Rules;
using Shouldly;

namespace Web.API.IntegrationTests.Architecture;

public class ArchitectureTests
{
    [Fact]
    public void Domain_Should_Not_HaveDependencyOn_Application()
    {
        var result = Types
            .InAssembly(typeof(Domain.SampleEntities.SampleEntity).Assembly)
            .ShouldNot()
            .HaveDependencyOn("Application")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue();
    }

    [Fact]
    public void Application_Should_Not_HaveDependencyOn_Infra()
    {
        var result = Types
            .InAssembly(typeof(global::Application.DependencyInjection).Assembly)
            .ShouldNot()
            .HaveDependencyOn("Infra")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue();
    }
}

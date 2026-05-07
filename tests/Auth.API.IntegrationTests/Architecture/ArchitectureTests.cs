using System.Linq;
using System.Reflection;
using Auth.API.Endpoints;
using Auth.Application.Abstractions.Messaging;
using NetArchTest.Rules;
using SharedKernel;
using Shouldly;

namespace Auth.API.IntegrationTests.Architecture;

public sealed class ArchitectureTests
{
    private static readonly Assembly AuthDomainAssembly =
        typeof(Auth.Domain.Audit.AuthAuditEvent).Assembly;

    private static readonly Assembly AuthApplicationAssembly =
        typeof(Auth.Application.DependencyInjection).Assembly;

    private static readonly Assembly AuthInfraAssembly =
        typeof(Auth.Infra.Database.AuthDbContext).Assembly;

    private static readonly Assembly AuthApiAssembly =
        typeof(Auth.API.Program).Assembly;

    [Fact]
    public void AuthDomain_ShouldNotDependOn_AuthApplication()
    {
        TestResult result = Types.InAssembly(AuthDomainAssembly)
            .ShouldNot()
            .HaveDependencyOn("Auth.Application")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            $"Failing types: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void AuthDomain_ShouldNotDependOn_AuthInfra()
    {
        TestResult result = Types.InAssembly(AuthDomainAssembly)
            .ShouldNot()
            .HaveDependencyOn("Auth.Infra")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            $"Failing types: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void AuthDomain_ShouldNotDependOn_EntityFrameworkCore()
    {
        TestResult result = Types.InAssembly(AuthDomainAssembly)
            .ShouldNot()
            .HaveDependencyOn("Microsoft.EntityFrameworkCore")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            $"Failing types: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void AuthApplication_ShouldNotDependOn_AuthInfra()
    {
        TestResult result = Types.InAssembly(AuthApplicationAssembly)
            .ShouldNot()
            .HaveDependencyOn("Auth.Infra")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            $"Failing types: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void AuthApplication_ShouldNotDependOn_AspNetCore()
    {
        TestResult result = Types.InAssembly(AuthApplicationAssembly)
            .ShouldNot()
            .HaveDependencyOn("Microsoft.AspNetCore")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            $"Failing types: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void AuthApplication_ShouldNotDependOn_OpenIddictServer()
    {
        TestResult result = Types.InAssembly(AuthApplicationAssembly)
            .ShouldNot()
            .HaveDependencyOn("OpenIddict.Server")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            $"Failing types: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void AuthInfra_ShouldNotDependOn_AuthApi()
    {
        TestResult result = Types.InAssembly(AuthInfraAssembly)
            .ShouldNot()
            .HaveDependencyOn("Auth.API")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            $"Failing types: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void AllAuthCommandHandlers_ShouldBe_Sealed()
    {
        TestResult result = Types.InAssembly(AuthApplicationAssembly)
            .That()
            .ImplementInterface(typeof(ICommandHandler<>))
            .Or()
            .ImplementInterface(typeof(ICommandHandler<,>))
            .Should()
            .BeSealed()
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            $"Failing types: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void AllAuthQueryHandlers_ShouldBe_Sealed()
    {
        TestResult result = Types.InAssembly(AuthApplicationAssembly)
            .That()
            .ImplementInterface(typeof(IQueryHandler<,>))
            .Should()
            .BeSealed()
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            $"Failing types: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void AllAuthEndpoints_ShouldBe_Sealed()
    {
        TestResult result = Types.InAssembly(AuthApiAssembly)
            .That()
            .ImplementInterface(typeof(IEndpoint))
            .Should()
            .BeSealed()
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            $"Failing types: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void AllAuthEntities_ShouldBe_Sealed()
    {
        TestResult result = Types.InAssembly(AuthDomainAssembly)
            .That()
            .Inherit(typeof(Entity))
            .Should()
            .BeSealed()
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            $"Failing types: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void JoinEntities_LiveOnlyIn_AuthInfra()
    {
        string[] joinNames = ["UserRole", "UserGroup", "GroupRole", "RolePermission"];

        // Must exist in Auth.Infra.
        Type[] infraJoins = AuthInfraAssembly.GetTypes()
            .Where(t => joinNames.Contains(t.Name) && t.IsClass)
            .ToArray();

        infraJoins.Select(t => t.Name).OrderBy(n => n).ShouldBe(
            joinNames.OrderBy(n => n),
            "Auth.Infra must contain all four EF join entities");

        // Must NOT exist in Auth.Domain.
        Type[] domainJoins = AuthDomainAssembly.GetTypes()
            .Where(t => joinNames.Contains(t.Name))
            .ToArray();

        domainJoins.ShouldBeEmpty(
            $"Join entities must not live in Auth.Domain: {string.Join(", ", domainJoins.Select(t => t.FullName))}");

        // Must NOT exist in Auth.Application.
        Type[] applicationJoins = AuthApplicationAssembly.GetTypes()
            .Where(t => joinNames.Contains(t.Name))
            .ToArray();

        applicationJoins.ShouldBeEmpty(
            $"Join entities must not live in Auth.Application: {string.Join(", ", applicationJoins.Select(t => t.FullName))}");
    }

    [Fact]
    public void AuthEndpoints_ShouldBeInternal()
    {
        Type[] endpointTypes = AuthApiAssembly.GetTypes()
            .Where(t => typeof(IEndpoint).IsAssignableFrom(t)
                && !t.IsInterface
                && !t.IsAbstract)
            .ToArray();

        endpointTypes.ShouldNotBeEmpty("Expected at least one IEndpoint implementation");

        Type[] publicEndpoints = endpointTypes.Where(t => t.IsPublic).ToArray();

        publicEndpoints.ShouldBeEmpty(
            $"Endpoint types must be internal, not public: {string.Join(", ", publicEndpoints.Select(t => t.FullName))}");
    }
}

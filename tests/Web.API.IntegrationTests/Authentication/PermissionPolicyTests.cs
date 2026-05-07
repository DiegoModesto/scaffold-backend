using System.Security.Claims;
using Infra.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Web.API.IntegrationTests.Authentication;

public sealed class PermissionPolicyTests
{
    private static PermissionPolicyProvider CreateProvider() =>
        new(Options.Create(new AuthorizationOptions()));

    [Fact]
    public async Task PolicyPrefix_GeneratesDynamicPolicy_PerPermission()
    {
        PermissionPolicyProvider provider = CreateProvider();

        AuthorizationPolicy? policy = await provider.GetPolicyAsync(
            $"{PermissionPolicyProvider.PolicyPrefix}foo.bar");

        policy.ShouldNotBeNull();
        policy.Requirements.OfType<PermissionRequirement>()
            .ShouldHaveSingleItem()
            .Permission.ShouldBe("foo.bar");
    }

    [Fact]
    public async Task Policy_RejectsPrincipalWithoutClaim()
    {
        var requirement = new PermissionRequirement("sample.read");
        var handler = new PermissionAuthorizationHandler();

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims: [], "test"));
        var context = new AuthorizationHandlerContext([requirement], principal, resource: null);

        await handler.HandleAsync(context);

        context.HasSucceeded.ShouldBeFalse();
    }

    [Fact]
    public async Task Policy_AcceptsPrincipalWithMatchingClaim()
    {
        var requirement = new PermissionRequirement("sample.read");
        var handler = new PermissionAuthorizationHandler();

        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("permission", "sample.read")],
            "test"));
        var context = new AuthorizationHandlerContext([requirement], principal, resource: null);

        await handler.HandleAsync(context);

        context.HasSucceeded.ShouldBeTrue();
    }
}

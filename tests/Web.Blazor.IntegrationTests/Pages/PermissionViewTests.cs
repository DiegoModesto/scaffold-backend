using System.Security.Claims;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Web.Blazor.Components.Shared;

namespace Web.Blazor.IntegrationTests.Pages;

/// <summary>
/// bUnit smoke tests for the <see cref="PermissionView"/> claim-gated wrapper. Bundle G
/// targeted PermissionView specifically because every admin page composes it; verifying
/// the gate works also verifies the rest of the page won't render to an unauthorised
/// user. We do not render the full admin pages (they pull MudBlazor + IAdminGatewayClient
/// which require a richer test scaffolding); a separate end-to-end suite can do that.
/// </summary>
public sealed class PermissionViewTests : TestContext
{
    public PermissionViewTests()
    {
        // Register the bUnit fake AuthenticationStateProvider so PermissionView's
        // [Inject] AuthenticationStateProvider resolves cleanly. Each test mutates the
        // returned principal via TestAuthorizationContext.
        Services.AddAuthorizationCore();
    }

    [Fact]
    public void RendersChildContent_WhenUserHasPermission()
    {
        TestAuthorizationContext authContext = this.AddTestAuthorization();
        authContext.SetAuthorized("alice");
        authContext.SetClaims(new Claim("permission", "users.read"));

        IRenderedComponent<PermissionView> cut = RenderComponent<PermissionView>(parameters => parameters
            .Add(p => p.Permission, "users.read")
            .AddChildContent("<span class=\"granted\">visible</span>"));

        cut.Markup.ShouldContain("granted");
        cut.Markup.ShouldContain("visible");
    }

    [Fact]
    public void HidesChildContent_AndShowsFallback_WhenUserMissingPermission()
    {
        TestAuthorizationContext authContext = this.AddTestAuthorization();
        authContext.SetAuthorized("bob");
        authContext.SetClaims(new Claim("permission", "users.read"));

        IRenderedComponent<PermissionView> cut = RenderComponent<PermissionView>(parameters => parameters
            .Add(p => p.Permission, "users.write")
            .AddChildContent("<span class=\"granted\">should-not-appear</span>")
            .Add<RenderFragment>(p => p.NoAccessFallback!, fallback => fallback.AddMarkupContent(0, "<span class=\"denied\">no access</span>")));

        cut.Markup.ShouldNotContain("should-not-appear");
        cut.Markup.ShouldContain("denied");
        cut.Markup.ShouldContain("no access");
    }

    [Fact]
    public void RendersNothing_WhenUserMissingPermission_AndNoFallback()
    {
        TestAuthorizationContext authContext = this.AddTestAuthorization();
        authContext.SetAuthorized("carol");
        authContext.SetClaims(new Claim("permission", "users.read"));

        IRenderedComponent<PermissionView> cut = RenderComponent<PermissionView>(parameters => parameters
            .Add(p => p.Permission, "users.write")
            .AddChildContent("<span class=\"granted\">hidden</span>"));

        cut.Markup.ShouldNotContain("hidden");
    }
}

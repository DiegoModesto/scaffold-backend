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
public sealed class PermissionViewTests : BunitContext
{
    public PermissionViewTests()
    {
        // AddAuthorization() (per test) registers bUnit's fake AuthenticationStateProvider and
        // cascades Task<AuthenticationState> into PermissionView. Each test mutates the
        // principal via BunitAuthorizationContext.
        Services.AddAuthorizationCore();
    }

    [Fact]
    public void RendersChildContent_WhenUserHasPermission()
    {
        BunitAuthorizationContext authContext = AddAuthorization();
        authContext.SetAuthorized("alice");
        authContext.SetClaims(new Claim("permission", "users.read"));

        IRenderedComponent<PermissionView> cut = Render<PermissionView>(parameters => parameters
            .Add(p => p.Permission, "users.read")
            .AddChildContent("<span class=\"granted\">visible</span>"));

        cut.Markup.ShouldContain("granted");
        cut.Markup.ShouldContain("visible");
    }

    [Fact]
    public void HidesChildContent_AndShowsFallback_WhenUserMissingPermission()
    {
        BunitAuthorizationContext authContext = AddAuthorization();
        authContext.SetAuthorized("bob");
        authContext.SetClaims(new Claim("permission", "users.read"));

        IRenderedComponent<PermissionView> cut = Render<PermissionView>(parameters => parameters
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
        BunitAuthorizationContext authContext = AddAuthorization();
        authContext.SetAuthorized("carol");
        authContext.SetClaims(new Claim("permission", "users.read"));

        IRenderedComponent<PermissionView> cut = Render<PermissionView>(parameters => parameters
            .Add(p => p.Permission, "users.write")
            .AddChildContent("<span class=\"granted\">hidden</span>"));

        cut.Markup.ShouldNotContain("hidden");
    }

    [Fact]
    public void RendersChildContent_WhenPermissionIsGrantedAfterFirstRender()
    {
        BunitAuthorizationContext authContext = AddAuthorization();
        authContext.SetAuthorized("dave");

        IRenderedComponent<PermissionView> cut = Render<PermissionView>(parameters => parameters
            .Add(p => p.Permission, "users.read")
            .AddChildContent("<span class=\"granted\">late</span>"));
        cut.Markup.ShouldNotContain("late");

        authContext.SetClaims(new Claim("permission", "users.read"));

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("late"));
    }
}

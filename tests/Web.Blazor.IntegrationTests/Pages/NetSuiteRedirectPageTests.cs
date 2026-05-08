using System.Security.Claims;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using Moq;
using Shouldly;
using Web.Blazor.Components.Pages.Admin;
using Web.Blazor.Gateway;

namespace Web.Blazor.IntegrationTests.Pages;

public sealed class NetSuiteRedirectPageTests : TestContext
{
    public NetSuiteRedirectPageTests()
    {
        Services.AddAuthorizationCore();
        Services.AddMudServices();
    }

    [Fact]
    public void Renders_AutoSubmitForm_WhenGatewayReturnsHtml()
    {
        TestAuthorizationContext authContext = this.AddTestAuthorization();
        authContext.SetAuthorized("admin");
        authContext.SetClaims(new Claim("permission", "users.write"));

        const string stubHtml = """
            <!DOCTYPE html><html><body onload="document.forms[0].submit()">
              <form method="post" action="https://system.netsuite.com/saml2/acs?account=1234567">
                <input type="hidden" name="SAMLResponse" value="STUB_BASE64" />
                <noscript><button type="submit">Continue to NetSuite</button></noscript>
              </form>
            </body></html>
            """;

        var gateway = new Mock<IAdminGatewayClient>();
        gateway
            .Setup(g => g.InitiateNetSuiteSsoAsync(It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(stubHtml);
        Services.AddSingleton(gateway.Object);

        Guid userId = Guid.NewGuid();
        var cut = RenderComponent<NetSuiteRedirect>(p => p.Add(c => c.Id, userId));

        cut.Markup.ShouldContain("name=\"SAMLResponse\"");
        cut.Markup.ShouldContain("system.netsuite.com/saml2/acs");
        cut.Markup.ShouldContain("document.forms[0].submit()");
        gateway.Verify(g =>
            g.InitiateNetSuiteSsoAsync(userId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public void RendersNothing_WhenUserMissingUsersWrite()
    {
        TestAuthorizationContext authContext = this.AddTestAuthorization();
        authContext.SetAuthorized("viewer");
        authContext.SetClaims(new Claim("permission", "users.read"));

        var gateway = new Mock<IAdminGatewayClient>();
        gateway
            .Setup(g => g.InitiateNetSuiteSsoAsync(It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("<form action=\"https://x\"></form>");
        Services.AddSingleton(gateway.Object);

        var cut = RenderComponent<NetSuiteRedirect>(p => p.Add(c => c.Id, Guid.NewGuid()));

        // No form is rendered when permission gate denies.
        cut.Markup.ShouldNotContain("https://x");
    }
}

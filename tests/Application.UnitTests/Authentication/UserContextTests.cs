using System.Security.Claims;
using Infra.Authentication;
using Microsoft.AspNetCore.Http;
using Moq;
using Shouldly;

namespace Application.UnitTests.Authentication;

public sealed class UserContextTests
{
    private static UserContext CreateContext(
        string? tenantClaim = null,
        string? tenantHeader = null)
    {
        var httpContext = new DefaultHttpContext();

        var claims = new List<Claim>();
        if (tenantClaim is not null)
        {
            claims.Add(new Claim("tenant_id", tenantClaim));
        }
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));

        if (tenantHeader is not null)
        {
            httpContext.Request.Headers["X-Forwarded-TenantId"] = tenantHeader;
        }

        var accessor = new Mock<IHttpContextAccessor>();
        accessor.SetupGet(a => a.HttpContext).Returns(httpContext);

        return new UserContext(accessor.Object);
    }

    [Fact]
    public void TenantId_FromClaim_WhenClaimPresent()
    {
        Guid expected = Guid.NewGuid();
        UserContext sut = CreateContext(tenantClaim: expected.ToString());

        sut.TenantId.ShouldBe(expected);
    }

    [Fact]
    public void TenantId_FromHeader_WhenClaimAbsent()
    {
        Guid expected = Guid.NewGuid();
        UserContext sut = CreateContext(tenantHeader: expected.ToString());

        sut.TenantId.ShouldBe(expected);
    }

    [Fact]
    public void TenantId_Null_WhenBothAbsent()
    {
        UserContext sut = CreateContext();

        sut.TenantId.ShouldBeNull();
    }

    [Fact]
    public void TenantId_PrefersClaim_WhenBothPresent()
    {
        Guid claimValue = Guid.NewGuid();
        Guid headerValue = Guid.NewGuid();
        UserContext sut = CreateContext(
            tenantClaim: claimValue.ToString(),
            tenantHeader: headerValue.ToString());

        sut.TenantId.ShouldBe(claimValue);
    }
}

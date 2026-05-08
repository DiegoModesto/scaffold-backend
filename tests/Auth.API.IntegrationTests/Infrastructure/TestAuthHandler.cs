using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Auth.API.IntegrationTests.Infrastructure;

/// <summary>
/// Test authentication scheme that builds a <see cref="ClaimsPrincipal"/> from the
/// <c>X-Test-Permissions</c> request header (comma-separated permission codes). When the
/// header is absent the request is treated as anonymous so policy-gated endpoints return 401.
/// </summary>
internal sealed class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "TestScheme";
    public const string PermissionsHeader = "X-Test-Permissions";
    public const string TenantHeader = "X-Test-TenantId";
    public const string UserHeader = "X-Test-UserId";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(PermissionsHeader, out Microsoft.Extensions.Primitives.StringValues header))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        string[] perms = header.ToString()
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        string userId = Request.Headers.TryGetValue(UserHeader, out var u) && !string.IsNullOrEmpty(u.ToString())
            ? u.ToString()
            : "test-user";

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId),
            new("sub", userId),
        };

        if (Request.Headers.TryGetValue(TenantHeader, out var t) && !string.IsNullOrEmpty(t.ToString()))
        {
            claims.Add(new Claim("tenant_id", t.ToString()));
        }

        foreach (string p in perms)
        {
            // Sentinel used by AuthWebApplicationFactory.CreateAuthorizedClient to keep
            // an authenticated identity that intentionally has zero permission claims.
            if (p == "_")
            {
                continue;
            }
            claims.Add(new Claim("permission", p));
        }

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

/// <summary>
/// Test policy provider that mirrors <c>PermissionPolicyProvider</c> but binds policies to
/// <see cref="TestAuthHandler.SchemeName"/> instead of OpenIddict validation, so the test
/// authentication ticket satisfies the policy's authentication-scheme requirement.
/// </summary>
internal sealed class TestPermissionPolicyProvider(IOptions<AuthorizationOptions> options)
    : Microsoft.AspNetCore.Authorization.DefaultAuthorizationPolicyProvider(options)
{
    public override Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (policyName == Auth.API.Endpoints.Saml.SamlAuthorizationPolicies.NetSuiteInitiate)
        {
            var samlPolicy = new AuthorizationPolicyBuilder()
                .AddAuthenticationSchemes(TestAuthHandler.SchemeName)
                .RequireAuthenticatedUser()
                .Build();
            return Task.FromResult<AuthorizationPolicy?>(samlPolicy);
        }

        const string prefix = Auth.API.Authorization.PermissionPolicyProvider.PolicyPrefix;
        if (!policyName.StartsWith(prefix, StringComparison.Ordinal))
        {
            return base.GetPolicyAsync(policyName);
        }

        string permission = policyName[prefix.Length..];
        var policy = new AuthorizationPolicyBuilder()
            .AddAuthenticationSchemes(TestAuthHandler.SchemeName)
            .RequireAuthenticatedUser()
            .AddRequirements(new Auth.API.Authorization.PermissionRequirement(permission))
            .Build();
        return Task.FromResult<AuthorizationPolicy?>(policy);
    }
}

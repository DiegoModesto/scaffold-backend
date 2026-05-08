using Microsoft.AspNetCore.Authorization;
using OpenIddict.Validation.AspNetCore;

namespace Auth.API.Endpoints.Saml;

internal static class SamlAuthorizationPolicies
{
    public const string NetSuiteInitiate = "saml:netsuite-initiate";

    public static AuthorizationPolicy NetSuiteInitiatePolicy { get; } =
        new AuthorizationPolicyBuilder()
            .AddAuthenticationSchemes(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)
            .RequireAuthenticatedUser()
            .Build();
}

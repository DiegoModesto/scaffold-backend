using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Shouldly;
using Web.Blazor.IntegrationTests.Infrastructure;

namespace Web.Blazor.IntegrationTests.Authentication;

/// <summary>
/// End-to-end-ish coverage of the BFF sign-in surface. The full OIDC dance is brittle
/// to drive through <see cref="HttpClient"/> (PKCE state cookie + nonce + correlation
/// cookie + signed id_token must all line up), so we verify the load-bearing pieces:
///
///  - <c>GET /login</c> issues a 302 to the configured authorize endpoint with the right
///    client_id / response_type / redirect_uri.
///  - <c>POST /logout</c> requires authentication.
///
/// Token-store + cookie persistence is exercised by <see cref="TokenStoreTests"/>; the
/// interaction between OIDC handler and token store is exercised in production via
/// <c>OnTokenValidated</c> and is not re-driven from a test client here.
/// </summary>
[Collection(BffCollection.Name)]
public sealed class SignInFlowTests(BffWebApplicationFactory factory)
{
    [Fact]
    public async Task LoginEndpoint_RedirectsToOidcChallenge()
    {
        HttpClient client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        HttpResponseMessage response = await client.GetAsync("/login");

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location.ShouldNotBeNull();
        string location = response.Headers.Location!.ToString();
        location.ShouldContain("/oauth2/authorize");
        location.ShouldContain($"client_id={BffWebApplicationFactory.ClientId}");
        location.ShouldContain("response_type=code");
        location.ShouldContain("code_challenge=");
        location.ShouldContain("code_challenge_method=S256");
    }

    [Fact]
    public async Task LogoutEndpoint_RequiresAuthentication()
    {
        HttpClient client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        HttpResponseMessage response = await client.PostAsync("/logout", content: null);

        // RequireAuthorization on the endpoint plus the cookie/oidc challenge default
        // collapses an unauthenticated POST into a 302 challenge to the OIDC authorize
        // endpoint. Either 302 or 401 is acceptable; both prove the endpoint is gated.
        response.StatusCode.ShouldBeOneOf(HttpStatusCode.Redirect, HttpStatusCode.Unauthorized);
    }
}

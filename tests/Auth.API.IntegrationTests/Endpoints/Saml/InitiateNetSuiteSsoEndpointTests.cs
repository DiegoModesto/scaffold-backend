using System.Net;
using System.Net.Http.Headers;
using Auth.API.IntegrationTests.Infrastructure;
using Auth.Application.NetSuite.InitiateSso;
using Auth.Domain.Permissions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shouldly;

namespace Auth.API.IntegrationTests.Endpoints.Saml;

[Trait("Category", "Integration")]
[Collection(AuthApiCollection.Name)]
public sealed class InitiateNetSuiteSsoEndpointTests(AuthWebApplicationFactory factory)
{
    private readonly AuthWebApplicationFactory _factory = factory;

    private WebApplicationFactory<Auth.API.Program> WithStubSigner(StubNetSuiteSamlSigner stub) =>
        _factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.RemoveAll<INetSuiteSamlSigner>();
            s.AddSingleton<INetSuiteSamlSigner>(stub);
        }));

    private static HttpClient AuthorizedClient(
        WebApplicationFactory<Auth.API.Program> f,
        Guid tenantId,
        Guid userId,
        params string[] permissions)
    {
        HttpClient client = f.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, tenantId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        string headerValue = permissions.Length == 0 ? "_" : string.Join(',', permissions);
        client.DefaultRequestHeaders.Add(TestAuthHandler.PermissionsHeader, headerValue);
        return client;
    }

    [Fact]
    public async Task Initiate_WithoutBearer_Returns401()
    {
        HttpClient http = _factory.CreateClient();

        HttpResponseMessage response = await http.PostAsync(
            "/saml/netsuite/initiate",
            new FormUrlEncodedContent(Array.Empty<KeyValuePair<string, string>>()));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Initiate_SelfSso_WithNetSuiteEmail_ReturnsAutoSubmitForm()
    {
        Guid tenantId = (await _factory.SeedTenantAsync(Guid.NewGuid(), "saml-self")).Id;
        var user = await _factory.SeedUserAsync(tenantId, Guid.NewGuid(), "self@example.com");

        // Set NetSuite email directly via DbContext
        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Auth.Infra.Database.AuthDbContext>();
            var u = await db.Users.FindAsync(user.Id);
            u!.SetNetSuiteEmail("self@netsuite.example");
            await db.SaveChangesAsync();
        }

        var stub = new StubNetSuiteSamlSigner(new SignedNetSuiteAssertion(
            "https://system.netsuite.com/saml2/acs?account=1234567",
            "STUB_BASE64",
            null));

        WebApplicationFactory<Auth.API.Program> wired = WithStubSigner(stub);
        HttpClient http = AuthorizedClient(wired, tenantId, user.Id);

        HttpResponseMessage response = await http.PostAsync(
            "/saml/netsuite/initiate",
            new FormUrlEncodedContent(Array.Empty<KeyValuePair<string, string>>()));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("text/html");
        string body = await response.Content.ReadAsStringAsync();
        body.ShouldContain("STUB_BASE64");
        body.ShouldContain("system.netsuite.com/saml2/acs");
        body.ShouldContain("name=\"SAMLResponse\"");
        body.ShouldContain("document.forms[0].submit()");
    }

    [Fact]
    public async Task Initiate_SelfSso_WithoutNetSuiteEmail_ReturnsProblem()
    {
        Guid tenantId = (await _factory.SeedTenantAsync(Guid.NewGuid(), "saml-no-email")).Id;
        var user = await _factory.SeedUserAsync(tenantId, Guid.NewGuid(), "no-email@example.com");

        var stub = new StubNetSuiteSamlSigner(null);
        WebApplicationFactory<Auth.API.Program> wired = WithStubSigner(stub);
        HttpClient http = AuthorizedClient(wired, tenantId, user.Id);

        HttpResponseMessage response = await http.PostAsync(
            "/saml/netsuite/initiate",
            new FormUrlEncodedContent(Array.Empty<KeyValuePair<string, string>>()));

        // Validation error returns 400 ProblemDetails via CustomResults.Problem.
        ((int)response.StatusCode).ShouldBeGreaterThanOrEqualTo(400);
        ((int)response.StatusCode).ShouldBeLessThan(500);
        string body = await response.Content.ReadAsStringAsync();
        body.ShouldContain("NetSuiteEmailMissing");
    }

    [Fact]
    public async Task Initiate_AdminOnBehalf_WithoutUsersWrite_Returns403()
    {
        Guid tenantId = (await _factory.SeedTenantAsync(Guid.NewGuid(), "saml-onbehalf-403")).Id;
        var caller = await _factory.SeedUserAsync(tenantId, Guid.NewGuid(), "caller@example.com");
        var target = await _factory.SeedUserAsync(tenantId, Guid.NewGuid(), "target@example.com");

        var stub = new StubNetSuiteSamlSigner(null);
        WebApplicationFactory<Auth.API.Program> wired = WithStubSigner(stub);
        HttpClient http = AuthorizedClient(wired, tenantId, caller.Id /* no permission */);

        var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("target_user_id", target.Id.ToString()),
        });
        HttpResponseMessage response = await http.PostAsync("/saml/netsuite/initiate", content);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Initiate_AdminOnBehalf_WithUsersWrite_ReturnsAutoSubmitForm()
    {
        Guid tenantId = (await _factory.SeedTenantAsync(Guid.NewGuid(), "saml-onbehalf-ok")).Id;
        var caller = await _factory.SeedUserAsync(tenantId, Guid.NewGuid(), "admin@example.com");
        var target = await _factory.SeedUserAsync(tenantId, Guid.NewGuid(), "target2@example.com");

        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Auth.Infra.Database.AuthDbContext>();
            var u = await db.Users.FindAsync(target.Id);
            u!.SetNetSuiteEmail("target2@netsuite.example");
            await db.SaveChangesAsync();
        }

        var stub = new StubNetSuiteSamlSigner(new SignedNetSuiteAssertion(
            "https://system.netsuite.com/saml2/acs?account=1234567",
            "ON_BEHALF_BASE64",
            null));

        WebApplicationFactory<Auth.API.Program> wired = WithStubSigner(stub);
        HttpClient http = AuthorizedClient(wired, tenantId, caller.Id, PermissionCodes.UsersWrite);

        var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("target_user_id", target.Id.ToString()),
        });
        HttpResponseMessage response = await http.PostAsync("/saml/netsuite/initiate", content);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        string body = await response.Content.ReadAsStringAsync();
        body.ShouldContain("ON_BEHALF_BASE64");
        // Verify the signer was called with the target user id (via stub recording).
        stub.LastUserId.ShouldBe(target.Id);
    }

    private sealed class StubNetSuiteSamlSigner : INetSuiteSamlSigner
    {
        private readonly SignedNetSuiteAssertion? _assertion;
        public Guid? LastUserId { get; private set; }
        public string? LastEmail { get; private set; }

        public StubNetSuiteSamlSigner(SignedNetSuiteAssertion? assertion)
        {
            _assertion = assertion;
        }

        public SignedNetSuiteAssertion Sign(string netSuiteEmail, Guid userId, string? relayState)
        {
            LastEmail = netSuiteEmail;
            LastUserId = userId;
            // Tests that hit a code path which never reaches the signer pass null.
            return _assertion ?? throw new InvalidOperationException(
                "Stub signer invoked but no assertion configured (test path expected to short-circuit before signing).");
        }
    }
}

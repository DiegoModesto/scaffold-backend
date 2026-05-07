# NetSuite SAML Outbound SSO Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add SAML 2.0 IdP-initiated SSO from `Auth.API` to NetSuite. After authenticating via Entra, a logged-in user can click "Open NetSuite" in the BFF backoffice and be redirected to NetSuite already signed in — no second password prompt.

**Architecture:** Extend `Auth.API` with a `/saml/netsuite/initiate` endpoint that, given an authenticated user (cookie session from the BFF call), generates and signs a SAML 2.0 assertion (NameID = `User.NetSuiteEmail`, Audience = configured NetSuite SP entityId) using `ITfoxtec.Identity.Saml2`, and returns an HTML form auto-POSTing to NetSuite's ACS URL. Add a `Pages/Admin/NetSuiteRedirect.razor` button in the BFF that calls the endpoint via the Gateway (route `/api/auth/saml/netsuite/initiate`). Auth.API stores per-user `NetSuiteEmail` already (Plan 1's User entity).

**Tech Stack:** .NET 10, `ITfoxtec.Identity.Saml2 4.x` (latest stable), X.509 cert for SAML assertion signing (RSA-SHA256), the existing Auth.API + BFF + Gateway from Plans 1-3.

**Out of scope (deferred):**
- Inbound SAML (NetSuite as IdP for Auth.API users) — out of scope per spec §3.
- SLO (Single Logout) — V2.
- Encrypted assertions — V2 (NetSuite supports unencrypted signed assertions by default).
- Per-tenant NetSuite account routing (V1: single NetSuite account ID per Auth.API instance).

---

## Context for the implementer

- Branch: `feature/netsuite-saml`, branched from `feature/blazor-bff` HEAD (stacks on Plans 2+3).
- Auth.API runs at `http://localhost:5100`. The Gateway proxies `/api/auth/saml/*` (a new route added by this plan).
- The User aggregate already has a `NetSuiteEmail` property + `SetNetSuiteEmail(string?)` method (Plan 1). The admin BFF page from Plan 3 lets admins set/clear it per user.
- NetSuite SP configuration: per-account, configured at `Setup → Integration → SAML Single Sign-on` in NetSuite UI. Needs:
  - **IdP entityId** (we choose: `https://auth.example.com/saml/netsuite`).
  - **IdP signing certificate** (X.509 public key) — uploaded to NetSuite once during onboarding.
  - **NetSuite ACS URL**: `https://system.netsuite.com/saml2/acs?account={accountId}`.
  - **Audience / EntityID**: NetSuite expects this audience: `https://system.netsuite.com/sp/{accountId}` (verify in NetSuite docs at integration time).
  - **NameID format**: `urn:oasis:names:tc:SAML:1.1:nameid-format:emailAddress`, value = NetSuite user's email.
- ITfoxtec.Identity.Saml2 is the .NET SAML lib of choice (well-documented, OSS). We use the `Saml2AuthnResponse` builder.

## Configuration keys (env / appsettings)

```
NetSuite__AccountId           # NetSuite account number, e.g., "1234567"
NetSuite__SamlAcsUrl          # https://system.netsuite.com/saml2/acs?account=1234567
NetSuite__SamlAudience        # https://system.netsuite.com/sp/1234567 (verify exact format)
NetSuite__SamlIssuer          # our IdP entityId, e.g., https://auth.example.com/saml/netsuite
NetSuite__SamlSigningCertificatePath        # path to PFX or PEM
NetSuite__SamlSigningCertificatePassword    # PFX password (env var)
```

Local dev: generate a self-signed X.509 cert via `openssl` and use a stub NetSuite SP (a WireMock route). Document the openssl command.

---

## File structure

```
src/Auth.Application/
└── NetSuite/
    └── InitiateSso/
        ├── InitiateNetSuiteSsoCommand.cs     # ICommand<NetSuiteSsoResponse>
        ├── InitiateNetSuiteSsoCommandValidator.cs
        ├── InitiateNetSuiteSsoCommandHandler.cs   # validates user has NetSuiteEmail, builds assertion
        └── NetSuiteSsoResponse.cs             # { AcsUrl, SignedSamlResponseBase64, RelayState? }

src/Auth.Infra/
└── NetSuite/
    ├── NetSuiteSamlOptions.cs                 # bound from "NetSuite" config section
    ├── INetSuiteSamlSigner.cs                 # interface used by handler (allows test fake)
    └── NetSuiteSamlSigner.cs                  # impl using ITfoxtec.Identity.Saml2

src/EntryPoints/Auth.API/
├── Endpoints/Saml/
│   └── InitiateNetSuiteSsoEndpoint.cs        # POST /saml/netsuite/initiate, returns auto-submit HTML
└── Authentication/
    └── BffSamlAuthorizationPolicy.cs          # admin-only OR self (any authenticated user can SSO themselves)

src/EntryPoints/Gateway/
└── (no new files; appsettings updated to add /api/auth/saml/* route)

src/EntryPoints/Web.Blazor/
├── Components/Pages/Admin/
│   └── NetSuiteRedirect.razor                  # standalone page: shows "Opening NetSuite..." spinner, auto-submits
└── Gateway/
    └── (extend IAdminGatewayClient with InitiateNetSuiteSsoAsync)

tests/
├── Auth.Application.UnitTests/NetSuite/InitiateSso/
│   ├── InitiateNetSuiteSsoCommandValidatorTests.cs
│   └── InitiateNetSuiteSsoCommandHandlerTests.cs
└── Auth.API.IntegrationTests/Endpoints/Saml/
    └── InitiateNetSuiteSsoEndpointTests.cs
```

---

## Phase 0 — Package + dev signing cert

### Task 0.1: Add ITfoxtec package

**Files:**
- Modify: `Directory.Packages.props`

```xml
<PackageVersion Include="ITfoxtec.Identity.Saml2" Version="4.16.1" />
<PackageVersion Include="ITfoxtec.Identity.Saml2.MvcCore" Version="4.16.1" />
```

Verify the latest version on nuget.org at implementation time; bump if newer is available.

```bash
dotnet restore BaseProjectScaffold.sln
git add Directory.Packages.props
git commit -m "chore: add ITfoxtec.Identity.Saml2 packages"
```

### Task 0.2: Generate dev SAML signing cert

**Files:**
- Create: `src/EntryPoints/Auth.API/dev-certs/netsuite-saml.pfx` (will be generated)
- Modify: `.gitignore` — exclude `dev-certs/` AND verify it's not already committed

The dev cert is generated by the implementer's local machine; **do not commit the PFX**. Provide a `.gitignore` rule and a script:

Create `scripts/generate-dev-saml-cert.sh`:

```bash
#!/bin/bash
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
OUT_DIR="$SCRIPT_DIR/../src/EntryPoints/Auth.API/dev-certs"
mkdir -p "$OUT_DIR"
openssl req -x509 -newkey rsa:2048 -days 365 -nodes \
  -keyout "$OUT_DIR/netsuite-saml.key" \
  -out "$OUT_DIR/netsuite-saml.crt" \
  -subj "/CN=auth.local/O=BaseProjectScaffold/C=BR"
openssl pkcs12 -export -out "$OUT_DIR/netsuite-saml.pfx" \
  -inkey "$OUT_DIR/netsuite-saml.key" \
  -in "$OUT_DIR/netsuite-saml.crt" \
  -password pass:dev-saml-password
echo "Dev SAML cert at $OUT_DIR/netsuite-saml.pfx (password: dev-saml-password)"
```

`chmod +x scripts/generate-dev-saml-cert.sh`. Document in CLAUDE.md and README that `bash scripts/generate-dev-saml-cert.sh` must be run once for local dev.

`.gitignore` add:
```
src/EntryPoints/Auth.API/dev-certs/
```

```bash
git add scripts/generate-dev-saml-cert.sh .gitignore
git commit -m "chore: dev SAML signing cert generation script"
```

---

## Phase 1 — `NetSuiteSamlSigner` (Auth.Infra)

### Task 1.1: `NetSuiteSamlOptions`

**File:** `src/Auth.Infra/NetSuite/NetSuiteSamlOptions.cs`

```csharp
namespace Auth.Infra.NetSuite;

internal sealed class NetSuiteSamlOptions
{
    public const string SectionName = "NetSuite";

    public string AccountId { get; set; } = "";
    public string SamlAcsUrl { get; set; } = "";
    public string SamlAudience { get; set; } = "";
    public string SamlIssuer { get; set; } = "";
    public string SamlSigningCertificatePath { get; set; } = "";
    public string? SamlSigningCertificatePassword { get; set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(AccountId)
        && !string.IsNullOrWhiteSpace(SamlAcsUrl)
        && !string.IsNullOrWhiteSpace(SamlAudience)
        && !string.IsNullOrWhiteSpace(SamlIssuer)
        && !string.IsNullOrWhiteSpace(SamlSigningCertificatePath);
}
```

Register in `Auth.Infra.DependencyInjection.AddAuthInfrastructure`:

```csharp
services.Configure<Auth.Infra.NetSuite.NetSuiteSamlOptions>(
    configuration.GetSection(Auth.Infra.NetSuite.NetSuiteSamlOptions.SectionName));
```

Commit: `feat(auth): NetSuiteSamlOptions configuration`

### Task 1.2: `INetSuiteSamlSigner` (TDD)

**Files:**
- Create: `src/Auth.Application/NetSuite/InitiateSso/INetSuiteSamlSigner.cs` (interface, used by handler)
- Create: `tests/Auth.Application.UnitTests/NetSuite/InitiateSso/NetSuiteSamlSignerTests.cs`
- Create: `src/Auth.Infra/NetSuite/NetSuiteSamlSigner.cs` (concrete impl)

`INetSuiteSamlSigner` lives in `Auth.Application` (the handler depends on it):

```csharp
namespace Auth.Application.NetSuite.InitiateSso;

public sealed record SignedNetSuiteAssertion(
    string AcsUrl,
    string SamlResponseBase64,
    string? RelayState);

public interface INetSuiteSamlSigner
{
    SignedNetSuiteAssertion Sign(string netSuiteEmail, Guid userId, string? relayState);
}
```

Concrete in `Auth.Infra`:

```csharp
using ITfoxtec.Identity.Saml2;
using ITfoxtec.Identity.Saml2.Schemas;
using Microsoft.Extensions.Options;
using System.Security.Cryptography.X509Certificates;
using Auth.Application.NetSuite.InitiateSso;

namespace Auth.Infra.NetSuite;

internal sealed class NetSuiteSamlSigner(IOptions<NetSuiteSamlOptions> options) : INetSuiteSamlSigner
{
    public SignedNetSuiteAssertion Sign(string netSuiteEmail, Guid userId, string? relayState)
    {
        var opts = options.Value;
        if (!opts.IsConfigured)
        {
            throw new InvalidOperationException("NetSuite SAML is not configured.");
        }

        var cert = X509CertificateLoader.LoadPkcs12FromFile(
            opts.SamlSigningCertificatePath,
            opts.SamlSigningCertificatePassword,
            X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet);

        var saml2Config = new Saml2Configuration
        {
            Issuer = opts.SamlIssuer,
            SigningCertificate = cert,
            SignatureAlgorithm = "http://www.w3.org/2001/04/xmldsig-more#rsa-sha256",
            AllowedAudienceUris = { opts.SamlAudience }
        };

        var response = new Saml2AuthnResponse(saml2Config)
        {
            Status = Saml2StatusCodes.Success,
            Destination = new Uri(opts.SamlAcsUrl),
            SessionIndex = userId.ToString("N"),
            ClaimsIdentity = new System.Security.Claims.ClaimsIdentity(new[]
            {
                new System.Security.Claims.Claim(ClaimTypes.NameIdentifier, netSuiteEmail)
            })
        };
        response.NameId = new Saml2NameIdentifier(netSuiteEmail, NameIdentifierFormats.Email);

        // Build the SAML XML and sign
        var samlXml = response.CreateAuthnResponse().OuterXml;
        // Sign (ITfoxtec handles via response.Bindings; the simplest is the HTTP-POST binding)
        // Use Saml2PostBinding for browser-driven POST to NetSuite ACS
        var binding = new Saml2PostBinding
        {
            RelayState = relayState
        };
        binding.Bind(response);
        var samlBase64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(binding.XmlDocument.OuterXml));

        return new SignedNetSuiteAssertion(opts.SamlAcsUrl, samlBase64, relayState);
    }
}
```

The `ITfoxtec.Identity.Saml2` API surface above is approximate — verify exact builder names by reading the package docs / source. The flow:
1. Build `Saml2AuthnResponse`.
2. Call `binding.Bind(response)` — signs and serializes.
3. Encode the resulting XML doc as base64 (this is the value that goes into the `<input name="SAMLResponse">` field of the HTML form).

**Tests** (`NetSuiteSamlSignerTests.cs`):
- `Sign_ProducesBase64SamlResponseWithSignedAssertion`: load a known dev cert, sign for a known email, decode the base64, parse as XML, assert: `<saml:Assertion>` exists, contains `<saml:Issuer>` matching `opts.SamlIssuer`, contains `<saml:NameID Format="...emailAddress">{email}</saml:NameID>`, contains a `<ds:Signature>` element.
- `Sign_Throws_WhenNotConfigured`.

For the test cert, generate one in test fixture's `IAsyncLifetime.InitializeAsync` via `CertificateRequest`. Or commit a known dev cert under `tests/Auth.Application.UnitTests/NetSuite/InitiateSso/test-saml.pfx` (generated; lives in test data). Document.

Commit: `feat(auth): NetSuiteSamlSigner with ITfoxtec.Identity.Saml2`

---

## Phase 2 — Application command + handler

### Task 2.1: `InitiateNetSuiteSsoCommand` + Handler

**Files:**
- Create: `src/Auth.Application/NetSuite/InitiateSso/InitiateNetSuiteSsoCommand.cs`
- Create: `src/Auth.Application/NetSuite/InitiateSso/InitiateNetSuiteSsoCommandHandler.cs`
- Create: `tests/Auth.Application.UnitTests/NetSuite/InitiateSso/InitiateNetSuiteSsoCommandHandlerTests.cs`

Command:

```csharp
public sealed record InitiateNetSuiteSsoCommand(Guid UserId, string? RelayState)
    : ICommand<SignedNetSuiteAssertion>;
```

Handler:

```csharp
public sealed class InitiateNetSuiteSsoCommandHandler(
    IAuthDbContext db,
    INetSuiteSamlSigner signer,
    ITenantContext tenantContext)
    : ICommandHandler<InitiateNetSuiteSsoCommand, SignedNetSuiteAssertion>
{
    public async Task<Result<SignedNetSuiteAssertion>> Handle(
        InitiateNetSuiteSsoCommand cmd, CancellationToken ct)
    {
        var user = await db.Users
            .FirstOrDefaultAsync(u => u.Id == cmd.UserId && u.TenantId == tenantContext.TenantId, ct);
        if (user is null) return Result.Failure<SignedNetSuiteAssertion>(UserErrors.NotFound(cmd.UserId));
        if (!user.IsActive) return Result.Failure<SignedNetSuiteAssertion>(UserErrors.Disabled);
        if (string.IsNullOrWhiteSpace(user.NetSuiteEmail))
            return Result.Failure<SignedNetSuiteAssertion>(UserErrors.NetSuiteEmailMissing);

        var assertion = signer.Sign(user.NetSuiteEmail, user.Id, cmd.RelayState);
        return assertion;
    }
}
```

Tests (5 scenarios):
- Happy path: user exists with NetSuiteEmail → returns SignedNetSuiteAssertion.
- User not found → `UserErrors.NotFound`.
- User disabled → `UserErrors.Disabled`.
- NetSuiteEmail missing → `UserErrors.NetSuiteEmailMissing`.
- Cross-tenant lookup blocked: user from tenant A, current TenantContext is tenant B → not found.

Use `Mock<INetSuiteSamlSigner>` for tests. Use `TestAuthDbContext` for DB.

Commit: `feat(auth): InitiateNetSuiteSsoCommand handler`

---

## Phase 3 — Auth.API endpoint

### Task 3.1: `/saml/netsuite/initiate` endpoint

**File:** `src/EntryPoints/Auth.API/Endpoints/Saml/InitiateNetSuiteSsoEndpoint.cs`

```csharp
internal sealed class InitiateNetSuiteSsoEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/saml/netsuite/initiate", async (
            HttpContext http,
            ICommandHandler<InitiateNetSuiteSsoCommand, SignedNetSuiteAssertion> handler,
            CancellationToken ct) =>
        {
            // Resolve user id from authenticated principal
            var subject = http.User.FindFirstValue(OpenIddictConstants.Claims.Subject);
            if (!Guid.TryParse(subject, out var userId)) return Results.Forbid();

            // Optional: target user override (admin can SSO on behalf of another user, gated by users.write)
            var targetParam = http.Request.Form["target_user_id"].ToString();
            if (!string.IsNullOrWhiteSpace(targetParam))
            {
                if (!http.User.HasClaim("permission", PermissionCodes.UsersWrite))
                    return Results.Forbid();
                userId = Guid.Parse(targetParam);
            }

            var relayState = http.Request.Form["RelayState"].ToString();
            var result = await handler.Handle(
                new InitiateNetSuiteSsoCommand(userId, string.IsNullOrEmpty(relayState) ? null : relayState),
                ct);
            return result.Match(
                assertion => RenderAutoSubmitForm(assertion),
                CustomResults.Problem);
        }).RequireAuthorization(b =>
        {
            b.AddAuthenticationSchemes(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);
            b.RequireAuthenticatedUser();
        });
    }

    private static IResult RenderAutoSubmitForm(SignedNetSuiteAssertion a)
    {
        var html = $$"""
            <!DOCTYPE html><html><body onload="document.forms[0].submit()">
              <form method="post" action="{{a.AcsUrl}}">
                <input type="hidden" name="SAMLResponse" value="{{a.SamlResponseBase64}}" />
                {{(a.RelayState is null ? "" : $"<input type=\"hidden\" name=\"RelayState\" value=\"{a.RelayState}\" />")}}
                <noscript><button type="submit">Continue to NetSuite</button></noscript>
              </form>
            </body></html>
            """;
        return Results.Content(html, "text/html");
    }
}
```

Tests (`tests/Auth.API.IntegrationTests/Endpoints/Saml/InitiateNetSuiteSsoEndpointTests.cs`):
- 401 without bearer token.
- 200 with HTML auto-submit form for self-SSO when user has NetSuiteEmail set.
- 400/422 with structured ProblemDetails when NetSuiteEmail missing.
- 403 when admin tries SSO on behalf of another user without `users.write` permission.
- 200 when admin with `users.write` SSOs on behalf of another user.

Stub `INetSuiteSamlSigner` via the integration test's DI override.

Commit: `feat(auth): /saml/netsuite/initiate endpoint with auto-submit form`

### Task 3.2: Wire `INetSuiteSamlSigner` into Auth.Infra DI

Modify `Auth.Infra.DependencyInjection.AddAuthInfrastructure`:

```csharp
services.AddScoped<INetSuiteSamlSigner, NetSuiteSamlSigner>();
```

Commit: `feat(auth): register NetSuiteSamlSigner in Infra DI`

---

## Phase 4 — Gateway route + BFF UI

### Task 4.1: Gateway route for `/api/auth/saml/*`

**Files:**
- Modify: `src/EntryPoints/Gateway/appsettings.Development.json`
- Modify: `compose.yaml`

Add YARP route:

```json
"auth-saml": {
  "ClusterId": "auth-cluster",
  "Match": { "Path": "/api/auth/saml/{**catch-all}" },
  "AuthorizationPolicy": "RequireBearer",
  "Transforms": [
    { "PathRemovePrefix": "/api/auth" }
  ]
}
```

Update compose env vars correspondingly.

Commit: `feat(gateway): route /api/auth/saml/* to Auth.API with bearer auth`

### Task 4.2: BFF "Open NetSuite" UI

**Files:**
- Create: `src/EntryPoints/Web.Blazor/Components/Pages/Admin/NetSuiteRedirect.razor`
- Modify: `src/EntryPoints/Web.Blazor/Components/Pages/Admin/UserDetail.razor` — add "Open NetSuite" button.
- Modify: `src/EntryPoints/Web.Blazor/Gateway/IAdminGatewayClient.cs` and `AdminGatewayClient.cs` — add `Task<string> InitiateNetSuiteSsoAsync(Guid? targetUserId, CancellationToken ct)` returning the HTML auto-submit form body.

UI flow:
1. User clicks "Open NetSuite" in `UserDetail.razor`.
2. Navigate to `/admin/users/{id}/netsuite-redirect` (the `NetSuiteRedirect.razor` page) with a query param.
3. The page server-renders, calls `AdminGatewayClient.InitiateNetSuiteSsoAsync(...)`, receives the HTML form body, renders it inside a `<MarkupString>` so the browser auto-submits to NetSuite.
4. (Optional) Show a "Opening NetSuite..." spinner that the form replaces.

`NetSuiteRedirect.razor` skeleton:

```razor
@page "/admin/users/{Id:guid}/netsuite-redirect"
@inject IAdminGatewayClient Gateway
@attribute [Authorize]

@if (string.IsNullOrEmpty(_html))
{
    <MudProgressCircular Indeterminate="true" />
    <MudText>Opening NetSuite for user...</MudText>
}
else
{
    @((MarkupString)_html)
}

@code {
    [Parameter] public Guid Id { get; set; }
    private string? _html;

    protected override async Task OnInitializedAsync()
    {
        _html = await Gateway.InitiateNetSuiteSsoAsync(Id, default);
    }
}
```

Self-SSO variant (any authenticated user, no target id): `/me/netsuite-redirect` page that calls `InitiateNetSuiteSsoAsync(null, ct)`.

Commit: `feat(bff): NetSuite SSO button and redirect page`

### Task 4.3: BFF tests

**File:** `tests/Web.Blazor.IntegrationTests/Pages/NetSuiteRedirectPageTests.cs`

Use bUnit. Mock `IAdminGatewayClient.InitiateNetSuiteSsoAsync` to return a stub HTML; assert the rendered output contains `<form action="...">` and the auto-submit script.

Commit: `test(bff): NetSuite redirect page renders auto-submit form`

---

## Phase 5 — Compose, docs, smoke

### Task 5.1: Compose env vars for NetSuite

**File:** `compose.yaml`

`auth.api` service env:

```yaml
- NetSuite__AccountId=${NETSUITE_ACCOUNT_ID:-1234567}
- NetSuite__SamlAcsUrl=${NETSUITE_SAML_ACS_URL:-https://system.netsuite.com/saml2/acs?account=1234567}
- NetSuite__SamlAudience=${NETSUITE_SAML_AUDIENCE:-https://system.netsuite.com/sp/1234567}
- NetSuite__SamlIssuer=${NETSUITE_SAML_ISSUER:-https://auth.local/saml/netsuite}
- NetSuite__SamlSigningCertificatePath=/app/dev-certs/netsuite-saml.pfx
- NetSuite__SamlSigningCertificatePassword=${NETSUITE_SAML_CERT_PASSWORD:-dev-saml-password}
```

Mount the dev cert into the Auth.API container:

```yaml
volumes:
  - ./src/EntryPoints/Auth.API/dev-certs:/app/dev-certs:ro
```

Commit: `chore(auth): NetSuite SAML env vars and dev-cert volume mount in compose`

### Task 5.2: Documentation

**Files:**
- Modify: `CLAUDE.md` — §9 (env vars), §13 (SAML pitfalls: clock skew tolerance, NameID format, ACS URL exact match).
- Modify: `README.md` — NetSuite SAML section EN + PT.

Document:
- Run `bash scripts/generate-dev-saml-cert.sh` once for local dev.
- Production: upload your real X.509 public cert to NetSuite under SAML SP setup.
- Plan 4 supports IdP-initiated only; SP-initiated is not in V1.

Commit: `docs: document NetSuite SAML in CLAUDE.md and README`

---

## Final verification

- [ ] **Step 1: Build + test**

```bash
dotnet build BaseProjectScaffold.sln -c Release
dotnet test BaseProjectScaffold.sln -c Release
```

Expected: 0 errors, 0 warnings. ~10 new tests (5 handler + 2 signer + 3 integration).

- [ ] **Step 2: Smoke**

```bash
bash scripts/generate-dev-saml-cert.sh
docker compose up -d --build redis auth-postgres auth.api gateway web.blazor
sleep 25
# Browse to BFF, login, navigate to a user with NetSuiteEmail set, click "Open NetSuite"
# The browser should attempt to auto-POST to NETSUITE_SAML_ACS_URL.
# Without a real NetSuite tenant, the POST will fail at NetSuite's side — verifying the assertion is generated and submitted is sufficient for V1 smoke.
docker compose down
```

- [ ] **Step 3: Tag**

```bash
git tag -a netsuite-saml-v1 -m "NetSuite SAML SSO complete (Plan 4 of 5)"
```

---

## Self-review

1. **Spec coverage** — Plan 4 covers spec §3 (outbound SAML to NetSuite), §7.3 (SAML flow), §10 (env vars). Out of scope: inbound SAML, SLO.

2. **Placeholder scan** — The ITfoxtec API calls in Task 1.2 are approximate; the implementer must verify against the package's docs/source at implementation time. Marked inline.

3. **Type consistency** — `SignedNetSuiteAssertion` record consistent across signer / handler / endpoint.

4. **Spec items with no task** — None. NetSuite outbound SSO is fully covered.

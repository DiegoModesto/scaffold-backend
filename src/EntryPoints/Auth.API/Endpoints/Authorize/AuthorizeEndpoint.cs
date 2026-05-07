using System.Diagnostics;
using System.Security.Claims;
using Auth.API.Authentication;
using Auth.API.Telemetry;
using Auth.Application.Abstractions.Identity;
using Auth.Application.Abstractions.Messaging;
using Auth.Application.Tenants.Resolve;
using Auth.Application.Users.SyncEntra;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;

namespace Auth.API.Endpoints.Authorize;

internal sealed class AuthorizeEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapMethods("/connect/authorize", ["GET", "POST"], AuthorizeAsync);
    }

    private static async Task<IResult> AuthorizeAsync(
        HttpContext http,
        IQueryHandler<ResolveTenantQuery, Auth.Domain.Tenants.Tenant> resolveTenant,
        ICommandHandler<SyncEntraUserCommand, Guid> syncUser,
        IPermissionResolver permissions,
        CancellationToken ct)
    {
        using Activity? activity = AuthActivitySource.Instance.StartActivity("Authorize");

        OpenIddictRequest request = http.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("OpenIddict server request not available.");

        AuthenticateResult entra = await http.AuthenticateAsync(EntraAuthenticationExtensions.SchemeName);
        if (!entra.Succeeded)
        {
            return Results.Challenge(
                new AuthenticationProperties
                {
                    RedirectUri = http.Request.Path + http.Request.QueryString
                },
                [EntraAuthenticationExtensions.SchemeName]);
        }

        ClaimsPrincipal principal = entra.Principal!;
        string? tidStr = principal.FindFirstValue("tid");
        string? oidStr = principal.FindFirstValue("oid");
        string? email = principal.FindFirstValue("preferred_username")
                     ?? principal.FindFirstValue("email")
                     ?? principal.FindFirstValue(ClaimTypes.Email);
        string displayName = principal.FindFirstValue("name") ?? email ?? "unknown";

        if (!Guid.TryParse(tidStr, out Guid tid)
         || !Guid.TryParse(oidStr, out Guid oid)
         || string.IsNullOrWhiteSpace(email))
        {
            return Results.Forbid();
        }

        activity?.SetTag("entra.tid", tid);
        activity?.SetTag("entra.oid", oid);

        var tenantR = await resolveTenant.Handle(new ResolveTenantQuery(tid), ct);
        if (tenantR.IsFailure)
        {
            return Results.Forbid();
        }

        activity?.SetTag("tenant.id", tenantR.Value.Id);

        var userR = await syncUser.Handle(
            new SyncEntraUserCommand(tenantR.Value.Id, oid, email, displayName), ct);
        if (userR.IsFailure)
        {
            return Results.Forbid();
        }

        activity?.SetTag("user.id", userR.Value);

        var permsR = await permissions.ResolveAsync(tenantR.Value.Id, userR.Value, ct);
        IReadOnlyCollection<string> perms = permsR.IsSuccess ? permsR.Value : [];

        var identity = new ClaimsIdentity(
            authenticationType: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
            nameType: OpenIddictConstants.Claims.Name,
            roleType: OpenIddictConstants.Claims.Role);

        identity.AddClaim(new Claim(OpenIddictConstants.Claims.Subject, userR.Value.ToString()));
        identity.AddClaim(new Claim("tenant_id", tenantR.Value.Id.ToString()));
        identity.AddClaim(new Claim(OpenIddictConstants.Claims.Email, email));
        identity.AddClaim(new Claim(OpenIddictConstants.Claims.Name, displayName));
        foreach (string p in perms)
        {
            identity.AddClaim(new Claim("permission", p));
        }

        foreach (Claim claim in identity.Claims)
        {
            claim.SetDestinations(
                OpenIddictConstants.Destinations.AccessToken,
                OpenIddictConstants.Destinations.IdentityToken);
        }

        var claimsPrincipal = new ClaimsPrincipal(identity);
        claimsPrincipal.SetScopes(request.GetScopes());

        return Results.SignIn(
            claimsPrincipal,
            authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }
}

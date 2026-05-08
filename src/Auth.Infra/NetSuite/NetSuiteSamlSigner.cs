using System.Security.Claims;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Auth.Application.NetSuite.InitiateSso;
using ITfoxtec.Identity.Saml2;
using ITfoxtec.Identity.Saml2.Schemas;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens.Saml2;

namespace Auth.Infra.NetSuite;

internal sealed class NetSuiteSamlSigner(IOptions<NetSuiteSamlOptions> options) : INetSuiteSamlSigner
{
    private const string RsaSha256SignatureAlgorithm =
        "http://www.w3.org/2001/04/xmldsig-more#rsa-sha256";

    public SignedNetSuiteAssertion Sign(string netSuiteEmail, Guid userId, string? relayState)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(netSuiteEmail);

        NetSuiteSamlOptions opts = options.Value;
        if (!opts.IsConfigured)
        {
            throw new InvalidOperationException("NetSuite SAML is not configured.");
        }

        X509Certificate2 cert = X509CertificateLoader.LoadPkcs12FromFile(
            opts.SamlSigningCertificatePath,
            opts.SamlSigningCertificatePassword,
            X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet);

        var saml2Config = new Saml2Configuration
        {
            Issuer = opts.SamlIssuer,
            SigningCertificate = cert,
            SignatureAlgorithm = RsaSha256SignatureAlgorithm,
        };
        saml2Config.AllowedAudienceUris.Add(opts.SamlAudience);

        var response = new Saml2AuthnResponse(saml2Config)
        {
            Status = Saml2StatusCodes.Success,
            Destination = new Uri(opts.SamlAcsUrl),
            SessionIndex = userId.ToString("N"),
            NameId = new Saml2NameIdentifier(netSuiteEmail, NameIdentifierFormats.Email),
        };

        var claimsIdentity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, netSuiteEmail),
            new Claim(ClaimTypes.Email, netSuiteEmail),
        ]);
        response.ClaimsIdentity = claimsIdentity;

        // Issue a security token for the assertion (covers Subject + AuthnStatement +
        // AudienceRestriction). Lifetime is short — IdP-initiated browser flow.
        response.CreateSecurityToken(
            appliesToAddress: opts.SamlAudience,
            authnContext: null,
            declAuthnContext: null,
            subjectConfirmationLifetime: 5,
            issuedTokenLifetime: 60);

        var binding = new Saml2PostBinding
        {
            RelayState = relayState,
        };
        binding.Bind(response);

        // The XmlDocument now contains the signed <samlp:Response>. The HTTP-POST binding
        // requires this XML to be base64-encoded into the SAMLResponse form field.
        string signedXml = binding.XmlDocument.OuterXml;
        string samlBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(signedXml));

        return new SignedNetSuiteAssertion(opts.SamlAcsUrl, samlBase64, relayState);
    }
}

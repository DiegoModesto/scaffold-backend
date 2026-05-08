using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Xml;
using Auth.Application.NetSuite.InitiateSso;
using Auth.Infra.NetSuite;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Auth.Application.UnitTests.NetSuite.InitiateSso;

public sealed class NetSuiteSamlSignerTests : IDisposable
{
    private readonly string _certPath;
    private const string CertPassword = "test-password";

    public NetSuiteSamlSignerTests()
    {
        _certPath = Path.Combine(Path.GetTempPath(), $"netsuite-saml-test-{Guid.NewGuid():N}.pfx");
        using RSA rsa = RSA.Create(2048);
        var req = new CertificateRequest(
            "CN=netsuite-saml-test",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        using X509Certificate2 cert = req.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-5),
            DateTimeOffset.UtcNow.AddYears(1));
        byte[] pfxBytes = cert.Export(X509ContentType.Pkcs12, CertPassword);
        File.WriteAllBytes(_certPath, pfxBytes);
    }

    public void Dispose()
    {
        if (File.Exists(_certPath))
        {
            File.Delete(_certPath);
        }
    }

    private NetSuiteSamlOptions BuildOptions() => new()
    {
        AccountId = "1234567",
        SamlAcsUrl = "https://system.netsuite.com/saml2/acs?account=1234567",
        SamlAudience = "https://system.netsuite.com/sp/1234567",
        SamlIssuer = "https://auth.local/saml/netsuite",
        SamlSigningCertificatePath = _certPath,
        SamlSigningCertificatePassword = CertPassword,
    };

    [Fact]
    public void Sign_ProducesBase64SamlResponseWithSignedAssertion()
    {
        NetSuiteSamlOptions opts = BuildOptions();
        var signer = new NetSuiteSamlSigner(Options.Create(opts));

        SignedNetSuiteAssertion result = signer.Sign(
            "user@netsuite.example",
            Guid.NewGuid(),
            relayState: "abc123");

        result.AcsUrl.ShouldBe(opts.SamlAcsUrl);
        result.RelayState.ShouldBe("abc123");
        result.SamlResponseBase64.ShouldNotBeNullOrWhiteSpace();

        // Decode base64, parse XML, assert structure.
        byte[] xmlBytes = Convert.FromBase64String(result.SamlResponseBase64);
        string xml = Encoding.UTF8.GetString(xmlBytes);

        var doc = new XmlDocument();
        doc.LoadXml(xml);

        var ns = new XmlNamespaceManager(doc.NameTable);
        ns.AddNamespace("samlp", "urn:oasis:names:tc:SAML:2.0:protocol");
        ns.AddNamespace("saml", "urn:oasis:names:tc:SAML:2.0:assertion");
        ns.AddNamespace("ds", "http://www.w3.org/2000/09/xmldsig#");

        XmlNode? assertion = doc.SelectSingleNode("//saml:Assertion", ns);
        assertion.ShouldNotBeNull("expected a <saml:Assertion> element");

        XmlNode? nameId = doc.SelectSingleNode("//saml:NameID", ns);
        nameId.ShouldNotBeNull();
        nameId!.InnerText.ShouldBe("user@netsuite.example");
        nameId.Attributes!["Format"]!.Value
            .ShouldBe("urn:oasis:names:tc:SAML:1.1:nameid-format:emailAddress");

        XmlNode? signature = doc.SelectSingleNode("//ds:Signature", ns);
        signature.ShouldNotBeNull("expected a <ds:Signature> element on the signed assertion");
    }

    [Fact]
    public void Sign_Throws_WhenNotConfigured()
    {
        var opts = new NetSuiteSamlOptions(); // empty
        var signer = new NetSuiteSamlSigner(Options.Create(opts));

        Should.Throw<InvalidOperationException>(() =>
            signer.Sign("user@example.com", Guid.NewGuid(), relayState: null));
    }
}

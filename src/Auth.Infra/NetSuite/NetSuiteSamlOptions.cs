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

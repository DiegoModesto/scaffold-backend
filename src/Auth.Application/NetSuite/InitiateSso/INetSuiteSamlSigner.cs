namespace Auth.Application.NetSuite.InitiateSso;

public sealed record SignedNetSuiteAssertion(
    string AcsUrl,
    string SamlResponseBase64,
    string? RelayState);

public interface INetSuiteSamlSigner
{
    SignedNetSuiteAssertion Sign(string netSuiteEmail, Guid userId, string? relayState);
}

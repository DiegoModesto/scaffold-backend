namespace Auth.Application.NetSuite.Dtos;

public sealed record SignedNetSuiteAssertion(
    string AcsUrl,
    string SamlResponseBase64,
    string? RelayState);

using Auth.Application.NetSuite.Dtos;

namespace Auth.Application.NetSuite.InitiateSso;

public interface INetSuiteSamlSigner
{
    SignedNetSuiteAssertion Sign(string netSuiteEmail, Guid userId, string? relayState);
}

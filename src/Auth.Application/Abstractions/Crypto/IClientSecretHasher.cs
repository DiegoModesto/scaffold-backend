namespace Auth.Application.Abstractions.Crypto;

public interface IClientSecretHasher
{
    string Hash(string secret);
    bool Verify(string secret, string hash);
}

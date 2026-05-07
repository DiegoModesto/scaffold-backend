using System.Security.Cryptography;
using Auth.Application.Abstractions.Crypto;

namespace Auth.Infra.Identity;

internal sealed class Pbkdf2ClientSecretHasher : IClientSecretHasher
{
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int Iterations = 100_000;

    private static readonly HashAlgorithmName Algorithm = HashAlgorithmName.SHA512;

    public string Hash(string secret)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(secret, salt, Iterations, Algorithm, HashSize);

        return $"{Convert.ToHexString(hash)}-{Convert.ToHexString(salt)}";
    }

    public bool Verify(string secret, string hash)
    {
        string[] parts = hash.Split('-');
        if (parts.Length != 2)
        {
            return false;
        }

        byte[] storedHash = Convert.FromHexString(parts[0]);
        byte[] salt = Convert.FromHexString(parts[1]);

        byte[] inputHash = Rfc2898DeriveBytes.Pbkdf2(secret, salt, Iterations, Algorithm, HashSize);
        return CryptographicOperations.FixedTimeEquals(storedHash, inputHash);
    }
}

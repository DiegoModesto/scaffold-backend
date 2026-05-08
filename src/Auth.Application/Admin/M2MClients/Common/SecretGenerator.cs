using System.Security.Cryptography;

namespace Auth.Application.Admin.M2MClients.Common;

internal static class SecretGenerator
{
    /// <summary>Generates a base64-URL-safe random 32-byte string suitable as an M2M client secret.</summary>
    public static string Generate()
    {
        byte[] bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }
}

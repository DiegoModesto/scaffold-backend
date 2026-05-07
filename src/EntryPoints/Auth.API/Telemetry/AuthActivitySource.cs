using System.Diagnostics;

namespace Auth.API.Telemetry;

internal static class AuthActivitySource
{
    public const string Name = "Auth.API";
    public static readonly ActivitySource Instance = new(Name);
}

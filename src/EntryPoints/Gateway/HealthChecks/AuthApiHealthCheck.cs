using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Gateway.HealthChecks;

internal sealed class AuthApiHealthCheck(IHttpClientFactory factory, IConfiguration configuration)
    : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var authority = configuration["Auth:Authority"];
        if (string.IsNullOrWhiteSpace(authority))
        {
            return HealthCheckResult.Unhealthy("Auth:Authority not configured.");
        }

        try
        {
            var client = factory.CreateClient("auth-health");
            using var resp = await client.GetAsync(
                new Uri(new Uri(authority), "/health/live"),
                cancellationToken);
            return resp.IsSuccessStatusCode
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy($"Auth.API returned {(int)resp.StatusCode}.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Auth.API unreachable.", ex);
        }
    }
}

using Auth.Application.Abstractions.Data;
using Auth.Domain.Permissions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Auth.Infra.Database;

/// <summary>
/// Idempotent seed for the static <see cref="PermissionCodes"/> catalog. Runs once on host
/// startup, inserts any missing permission rows, and exits. The hosted service is registered
/// as a singleton (per IHostedService contract) so it resolves a fresh scope to obtain the
/// scoped <see cref="IAuthDbContext"/>. The seed depends on the abstraction (not the concrete
/// <c>AuthDbContext</c>) so it can be exercised with the InMemory <c>TestAuthDbContext</c>.
/// </summary>
internal sealed class PermissionSeedHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<PermissionSeedHostedService> logger)
    : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        IAuthDbContext db = scope.ServiceProvider.GetRequiredService<IAuthDbContext>();

        List<string> existingCodes = await db.Permissions
            .Select(p => p.Code)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var existingSet = existingCodes.ToHashSet(StringComparer.Ordinal);

        List<Permission> toAdd = PermissionCodes.All
            .Where(p => !existingSet.Contains(p.Code))
            .Select(p => Permission.Create(p.Code, p.Description))
            .ToList();

        if (toAdd.Count == 0)
        {
            logger.LogDebug(
                "Permission seed: all {Count} codes already present.",
                PermissionCodes.All.Count);
            return;
        }

        foreach (Permission permission in toAdd)
        {
            db.Permissions.Add(permission);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Permission seed: inserted {Count} new permission codes.",
            toAdd.Count);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

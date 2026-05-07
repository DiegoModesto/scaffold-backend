using Auth.Application.Abstractions.Identity;
using Auth.Domain.Users;
using Auth.Infra.Database;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Auth.Infra.Identity;

internal sealed class PermissionResolver(AuthDbContext db) : IPermissionResolver
{
    public async Task<Result<IReadOnlyCollection<string>>> ResolveAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        bool userExists = await db.Users
            .AnyAsync(
                u => u.TenantId == tenantId && u.Id == userId && u.IsActive,
                cancellationToken)
            .ConfigureAwait(false);

        if (!userExists)
        {
            return Result.Failure<IReadOnlyCollection<string>>(UserErrors.NotFound(userId));
        }

        var directRoleIds = db.UserRoles
            .Where(ur => ur.UserId == userId)
            .Select(ur => ur.RoleId);

        var groupIds = db.UserGroups
            .Where(ug => ug.UserId == userId)
            .Select(ug => ug.GroupId);

        var groupRoleIds = db.GroupRoles
            .Where(gr => groupIds.Contains(gr.GroupId))
            .Select(gr => gr.RoleId);

        var allRoleIds = directRoleIds.Union(groupRoleIds).Distinct();

        var permissionIds = db.RolePermissions
            .Where(rp => allRoleIds.Contains(rp.RoleId))
            .Select(rp => rp.PermissionId)
            .Distinct();

        List<string> codes = await db.Permissions
            .Where(p => permissionIds.Contains(p.Id))
            .Select(p => p.Code)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Result.Success<IReadOnlyCollection<string>>(codes);
    }
}

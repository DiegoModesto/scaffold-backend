using Auth.Application.Abstractions.Data;
using Auth.Application.Abstractions.Identity;
using Auth.Domain.Groups;
using Auth.Domain.Roles;
using Auth.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Auth.Infra.Identity;

internal sealed class PermissionResolver(IAuthDbContext db) : IPermissionResolver
{
    public async Task<IReadOnlyCollection<string>> ResolveAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        User? user = await db.Users
            .FirstOrDefaultAsync(
                u => u.TenantId == tenantId && u.Id == userId && u.IsActive,
                cancellationToken);

        if (user is null)
        {
            return Array.Empty<string>();
        }

        var directRoleIds = user.RoleIds.ToList();
        var groupIds = user.GroupIds.ToList();

        List<Group> groups = await db.Groups
            .Where(g => groupIds.Contains(g.Id))
            .ToListAsync(cancellationToken);

        List<Guid> groupRoleIds = groups.SelectMany(g => g.RoleIds).Distinct().ToList();

        var allRoleIds = directRoleIds.Union(groupRoleIds).Distinct().ToList();

        List<Role> roles = await db.Roles
            .Where(r => allRoleIds.Contains(r.Id))
            .ToListAsync(cancellationToken);

        List<Guid> permissionIds = roles.SelectMany(r => r.PermissionIds).Distinct().ToList();

        return await db.Permissions
            .Where(p => permissionIds.Contains(p.Id))
            .Select(p => p.Code)
            .ToListAsync(cancellationToken);
    }
}

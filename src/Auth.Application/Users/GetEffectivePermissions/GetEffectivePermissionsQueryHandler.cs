using Auth.Application.Abstractions.Data;
using Auth.Application.Abstractions.Messaging;
using Auth.Domain.Groups;
using Auth.Domain.Roles;
using Auth.Domain.Users;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Auth.Application.Users.GetEffectivePermissions;

public sealed class GetEffectivePermissionsQueryHandler(IAuthDbContext db)
    : IQueryHandler<GetEffectivePermissionsQuery, IReadOnlyCollection<string>>
{
    public async Task<Result<IReadOnlyCollection<string>>> Handle(
        GetEffectivePermissionsQuery query,
        CancellationToken cancellationToken)
    {
        User? user = await db.Users
            .FirstOrDefaultAsync(
                u => u.TenantId == query.TenantId && u.Id == query.UserId && u.IsActive,
                cancellationToken);

        if (user is null)
        {
            return Result.Failure<IReadOnlyCollection<string>>(UserErrors.NotFound(query.UserId));
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

        List<string> codes = await db.Permissions
            .Where(p => permissionIds.Contains(p.Id))
            .Select(p => p.Code)
            .ToListAsync(cancellationToken);

        return Result.Success<IReadOnlyCollection<string>>(codes);
    }
}

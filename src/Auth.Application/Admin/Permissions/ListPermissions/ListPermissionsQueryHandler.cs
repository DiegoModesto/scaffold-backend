using Auth.Application.Abstractions.Data;
using Auth.Application.Abstractions.Messaging;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Auth.Application.Admin.Permissions.ListPermissions;

public sealed class ListPermissionsQueryHandler(IAuthDbContext db)
    : IQueryHandler<ListPermissionsQuery, IReadOnlyCollection<PermissionResponse>>
{
    public async Task<Result<IReadOnlyCollection<PermissionResponse>>> Handle(
        ListPermissionsQuery query,
        CancellationToken cancellationToken)
    {
        List<PermissionResponse> items = await db.Permissions
            .OrderBy(p => p.Code)
            .Select(p => new PermissionResponse(p.Id, p.Code, p.Description))
            .ToListAsync(cancellationToken);

        return items;
    }
}

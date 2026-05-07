using Auth.Application.Abstractions.Data;
using Auth.Application.Abstractions.Messaging;
using Auth.Application.Abstractions.Tenancy;
using Auth.Domain.Groups;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Auth.Application.Admin.Groups.Delete;

public sealed class DeleteGroupCommandHandler(IAuthDbContext db, ITenantContext tenant)
    : ICommandHandler<DeleteGroupCommand>
{
    public async Task<Result> Handle(DeleteGroupCommand command, CancellationToken cancellationToken)
    {
        Guid tenantId = tenant.TenantId;
        Group? group = await db.Groups.FirstOrDefaultAsync(
            g => g.Id == command.Id && g.TenantId == tenantId && !g.IsDeleted,
            cancellationToken);

        if (group is null)
        {
            return Result.Failure(GroupErrors.NotFound(command.Id));
        }

        group.Delete();
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

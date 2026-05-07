using SharedKernel;

namespace Auth.Application.Abstractions.Identity;

public interface IPermissionResolver
{
    Task<Result<IReadOnlyCollection<string>>> ResolveAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken cancellationToken);
}

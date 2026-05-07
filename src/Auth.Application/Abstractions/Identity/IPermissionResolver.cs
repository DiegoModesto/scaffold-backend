namespace Auth.Application.Abstractions.Identity;

public interface IPermissionResolver
{
    Task<IReadOnlyCollection<string>> ResolveAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken);
}

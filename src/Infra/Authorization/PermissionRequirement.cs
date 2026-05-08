using Microsoft.AspNetCore.Authorization;

namespace Infra.Authorization;

public sealed class PermissionRequirement(string permission) : IAuthorizationRequirement
{
    public string Permission { get; } = permission;
}

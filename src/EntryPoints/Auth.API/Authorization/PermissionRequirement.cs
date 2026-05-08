using Microsoft.AspNetCore.Authorization;

namespace Auth.API.Authorization;

internal sealed class PermissionRequirement(string permission) : IAuthorizationRequirement
{
    public string Permission { get; } = permission;
}

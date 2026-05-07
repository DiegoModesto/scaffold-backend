using SharedKernel;

namespace Auth.Domain.Permissions;

public static class PermissionErrors
{
    public static Error NotFound(Guid id) => Error.NotFound(
        "Permission.NotFound",
        $"The permission with id '{id}' was not found.");
}

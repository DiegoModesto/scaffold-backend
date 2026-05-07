using SharedKernel;

namespace Auth.Domain.Roles;

public static class RoleErrors
{
    public static readonly Error NameAlreadyTaken = Error.Conflict(
        "Role.NameAlreadyTaken",
        "A role with the same name already exists in this tenant.");

    public static Error NotFound(Guid id) => Error.NotFound(
        "Role.NotFound",
        $"The role with id '{id}' was not found.");
}

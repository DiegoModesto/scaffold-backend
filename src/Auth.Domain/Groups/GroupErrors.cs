using SharedKernel;

namespace Auth.Domain.Groups;

public static class GroupErrors
{
    public static readonly Error NameAlreadyTaken = Error.Conflict(
        "Group.NameAlreadyTaken",
        "A group with the same name already exists in this tenant.");

    public static Error NotFound(Guid id) => Error.NotFound(
        "Group.NotFound",
        $"The group with id '{id}' was not found.");
}

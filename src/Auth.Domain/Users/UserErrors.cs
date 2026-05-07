using SharedKernel;

namespace Auth.Domain.Users;

public static class UserErrors
{
    public static readonly Error Disabled = Error.Forbidden(
        "User.Disabled",
        "The user is disabled and cannot perform this action.");

    public static readonly Error NetSuiteEmailMissing = Error.Validation(
        "User.NetSuiteEmailMissing",
        "The user does not have a NetSuite email configured.");

    public static readonly Error EmailAlreadyTaken = Error.Conflict(
        "User.EmailAlreadyTaken",
        "A user with the same email already exists in this tenant.");

    public static Error NotFound(Guid id) => Error.NotFound(
        "User.NotFound",
        $"The user with id '{id}' was not found.");
}

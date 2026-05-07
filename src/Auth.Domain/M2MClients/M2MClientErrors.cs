using SharedKernel;

namespace Auth.Domain.M2MClients;

public static class M2MClientErrors
{
    public static readonly Error InvalidSecret = Error.Forbidden(
        "M2MClient.InvalidSecret",
        "The provided client secret is invalid.");

    public static readonly Error Inactive = Error.Forbidden(
        "M2MClient.Inactive",
        "The machine-to-machine client is inactive.");

    public static Error NotFound(Guid id) => Error.NotFound(
        "M2MClient.NotFound",
        $"The machine-to-machine client with id '{id}' was not found.");
}

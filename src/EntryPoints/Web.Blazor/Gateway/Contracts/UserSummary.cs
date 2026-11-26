namespace Web.Blazor.Gateway.Contracts;

public sealed record UserSummary(
    Guid Id,
    string Email,
    string DisplayName,
    bool IsActive);

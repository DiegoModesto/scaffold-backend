namespace Web.Blazor.Gateway.Contracts;

public sealed record RoleSummary(
    Guid Id,
    string Name,
    string Description);

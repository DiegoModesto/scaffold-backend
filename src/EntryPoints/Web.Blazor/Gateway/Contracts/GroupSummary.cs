namespace Web.Blazor.Gateway.Contracts;

public sealed record GroupSummary(
    Guid Id,
    string Name,
    string Description,
    bool HasEntraLink);

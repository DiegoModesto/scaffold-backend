namespace Web.Blazor.Gateway.Contracts;

public sealed record M2MClientSummary(
    Guid Id,
    string ClientId,
    string DisplayName,
    bool IsActive);

namespace Web.Blazor.Gateway.Contracts;

public sealed record CreateM2MClientResponse(
    Guid Id,
    string ClientId,
    string ClientSecret);

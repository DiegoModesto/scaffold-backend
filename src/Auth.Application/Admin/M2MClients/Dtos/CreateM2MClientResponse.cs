namespace Auth.Application.Admin.M2MClients.Dtos;

public sealed record CreateM2MClientResponse(Guid Id, string ClientId, string ClientSecret);

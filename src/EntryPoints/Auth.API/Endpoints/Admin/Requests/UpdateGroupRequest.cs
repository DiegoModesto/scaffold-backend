namespace Auth.API.Endpoints.Admin.Requests;

internal sealed record UpdateGroupRequest(string Name, string Description, Guid? EntraGroupId);

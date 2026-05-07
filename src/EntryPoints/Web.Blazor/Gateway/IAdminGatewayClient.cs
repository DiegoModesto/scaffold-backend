using Web.Blazor.Gateway.Contracts;

namespace Web.Blazor.Gateway;

public interface IAdminGatewayClient
{
    // Users
    Task<PagedResponse<UserSummary>> ListUsersAsync(int page, int pageSize, string? search, CancellationToken ct);

    Task<UserDetailResponse?> GetUserAsync(Guid id, CancellationToken ct);

    Task<Guid> PreProvisionUserAsync(string email, string displayName, string? netSuiteEmail, CancellationToken ct);

    Task DisableUserAsync(Guid id, CancellationToken ct);

    Task EnableUserAsync(Guid id, CancellationToken ct);

    Task SetNetSuiteEmailAsync(Guid id, string? netSuiteEmail, CancellationToken ct);

    Task AssignRoleToUserAsync(Guid userId, Guid roleId, CancellationToken ct);

    Task RevokeRoleFromUserAsync(Guid userId, Guid roleId, CancellationToken ct);

    Task AddUserToGroupAsync(Guid userId, Guid groupId, CancellationToken ct);

    Task RemoveUserFromGroupAsync(Guid userId, Guid groupId, CancellationToken ct);

    // Groups
    Task<PagedResponse<GroupSummary>> ListGroupsAsync(int page, int pageSize, string? search, CancellationToken ct);

    Task<GroupDetailResponse?> GetGroupAsync(Guid id, CancellationToken ct);

    Task<Guid> CreateGroupAsync(string name, string description, Guid? entraGroupId, CancellationToken ct);

    Task UpdateGroupAsync(Guid id, string name, string description, Guid? entraGroupId, CancellationToken ct);

    Task DeleteGroupAsync(Guid id, CancellationToken ct);

    Task AssignRoleToGroupAsync(Guid groupId, Guid roleId, CancellationToken ct);

    Task RevokeRoleFromGroupAsync(Guid groupId, Guid roleId, CancellationToken ct);

    // Roles
    Task<PagedResponse<RoleSummary>> ListRolesAsync(int page, int pageSize, string? search, CancellationToken ct);

    Task<RoleDetailResponse?> GetRoleAsync(Guid id, CancellationToken ct);

    Task<Guid> CreateRoleAsync(string name, string description, CancellationToken ct);

    Task UpdateRoleAsync(Guid id, string name, string description, CancellationToken ct);

    Task DeleteRoleAsync(Guid id, CancellationToken ct);

    Task AssignPermissionToRoleAsync(Guid roleId, Guid permissionId, CancellationToken ct);

    Task RevokePermissionFromRoleAsync(Guid roleId, Guid permissionId, CancellationToken ct);

    // Permissions
    Task<IReadOnlyCollection<PermissionResponse>> ListPermissionsAsync(CancellationToken ct);

    // M2M Clients
    Task<PagedResponse<M2MClientSummary>> ListM2MClientsAsync(int page, int pageSize, CancellationToken ct);

    Task<M2MClientDetailResponse?> GetM2MClientAsync(Guid id, CancellationToken ct);

    Task<CreateM2MClientResponse> CreateM2MClientAsync(string clientId, string displayName, IReadOnlyCollection<string> allowedScopes, CancellationToken ct);

    Task<string> RegenerateM2MClientSecretAsync(Guid id, CancellationToken ct);

    Task DeactivateM2MClientAsync(Guid id, CancellationToken ct);

    // Audit
    Task<PagedResponse<AuthAuditEventResponse>> ListAuditEventsAsync(
        int page,
        int pageSize,
        Guid? userId,
        string? eventType,
        DateTimeOffset? from,
        DateTimeOffset? to,
        CancellationToken ct);
}

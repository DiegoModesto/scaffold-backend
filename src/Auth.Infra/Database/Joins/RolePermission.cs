namespace Auth.Infra.Database.Joins;

internal sealed class RolePermission
{
    public Guid RoleId { get; init; }
    public Guid PermissionId { get; init; }
}

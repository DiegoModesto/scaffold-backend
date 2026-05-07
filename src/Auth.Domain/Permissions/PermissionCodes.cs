namespace Auth.Domain.Permissions;

public static class PermissionCodes
{
    public const string UsersRead = "users.read";
    public const string UsersWrite = "users.write";
    public const string GroupsRead = "groups.read";
    public const string GroupsWrite = "groups.write";
    public const string RolesRead = "roles.read";
    public const string RolesWrite = "roles.write";
    public const string M2MClientsRead = "m2mclients.read";
    public const string M2MClientsWrite = "m2mclients.write";
    public const string AuditRead = "audit.read";

    public static IReadOnlyCollection<(string Code, string Description)> All { get; } =
    [
        (UsersRead, "Read users"),
        (UsersWrite, "Create and modify users"),
        (GroupsRead, "Read groups"),
        (GroupsWrite, "Create and modify groups"),
        (RolesRead, "Read roles"),
        (RolesWrite, "Create and modify roles"),
        (M2MClientsRead, "Read machine-to-machine clients"),
        (M2MClientsWrite, "Create and modify machine-to-machine clients"),
        (AuditRead, "Read audit events"),
    ];
}

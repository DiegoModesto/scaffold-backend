namespace Auth.Domain.Audit;

public enum AuthAuditEventType
{
    LoginSucceeded,
    LoginFailed,
    UserProvisioned,
    UserDisabled,
    UserEnabled,
    RoleAssigned,
    RoleRevoked,
    GroupCreated,
    M2MTokenIssued,
}

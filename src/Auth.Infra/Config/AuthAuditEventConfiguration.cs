using Auth.Domain.Audit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Auth.Infra.Config;

internal sealed class AuthAuditEventConfiguration : IEntityTypeConfiguration<AuthAuditEvent>
{
    public void Configure(EntityTypeBuilder<AuthAuditEvent> builder)
    {
        // Note: no AbstractConfiguration / soft-delete query filter — the audit log
        // intentionally retains soft-deleted rows for compliance.
        builder.ToTable("auth_audit_events");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.TenantId).IsRequired();
        builder.Property(a => a.EventType).HasConversion<string>().HasMaxLength(50).IsRequired();
        builder.Property(a => a.Ip).HasMaxLength(45).IsRequired();
        builder.Property(a => a.UserAgent).HasMaxLength(500).IsRequired();
        builder.Property(a => a.Detail).HasColumnType("jsonb").IsRequired();
        builder.Property(a => a.OccurredAt).IsRequired();
        builder.Property(a => a.CreatedAt).IsRequired();
        builder.Property(a => a.DeletedAt);
        builder.Property(a => a.IsDeleted).IsRequired();

        builder.HasIndex(a => new { a.TenantId, a.OccurredAt });
    }
}

using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistences.Configurations;

public sealed class AuditEventConfiguration : IEntityTypeConfiguration<AuditEvent>
{
  public void Configure(EntityTypeBuilder<AuditEvent> builder)
  {
    builder.ToTable("audit_events");
    builder.HasKey(x => x.Id);
    builder.Property(x => x.EventType)
      .HasMaxLength(100)
      .IsRequired();
    builder.Property(x => x.Details)
      .HasColumnType("jsonb");
    builder.HasIndex(x => x.EventType);
    builder.HasIndex(x => x.OccurredAt);
    // agent_id and task_id have no foreign keys on purpose: the audit trail must outlive agents and tasks.
  }
}

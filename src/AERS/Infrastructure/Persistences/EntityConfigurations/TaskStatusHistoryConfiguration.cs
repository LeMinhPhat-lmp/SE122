using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistences.Configurations;

public sealed class TaskStatusHistoryConfiguration : IEntityTypeConfiguration<TaskStatusHistory>
{
  public void Configure(EntityTypeBuilder<TaskStatusHistory> builder)
  {
    builder.ToTable("task_status_history");
    builder.HasKey(history => history.Id);

    builder.Property(history => history.FromStatus)
      .HasConversion<short>()
      .HasColumnType("smallint");
    builder.Property(history => history.ToStatus)
      .HasConversion<short>()
      .HasColumnType("smallint");

    // Tasks are never deleted, so restrict is enough.
    builder.HasOne<AgentTask>().WithMany().HasForeignKey(history => history.TaskId).OnDelete(DeleteBehavior.Restrict);
    builder.HasIndex(history => new { history.TaskId, history.ChangedAt });
  }
}

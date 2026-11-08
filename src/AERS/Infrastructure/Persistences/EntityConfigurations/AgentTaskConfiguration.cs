using System.Text.Json;
using System.Text.Json.Serialization;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Infrastructure.Persistences.Configurations;

public sealed class AgentTaskConfiguration : IEntityTypeConfiguration<AgentTask>
{
  private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
  {
    DefaultIgnoreCondition = JsonIgnoreCondition.Never
  };

  // Plain static methods: expression trees used by value converters cannot contain optional arguments.
  private static string SerializeChain(List<Guid> chain) => JsonSerializer.Serialize(chain, JsonOptions);
  private static List<Guid> DeserializeChain(string json) => JsonSerializer.Deserialize<List<Guid>>(json, JsonOptions) ?? new List<Guid>();
  private static string SerializeError(TaskError error) => JsonSerializer.Serialize(error, JsonOptions);
  private static TaskError DeserializeError(string json) => JsonSerializer.Deserialize<TaskError>(json, JsonOptions)!;

  public void Configure(EntityTypeBuilder<AgentTask> builder)
  {
    builder.ToTable("tasks");
    builder.HasKey(task => task.Id);

    builder.Property(task => task.CapabilityName)
      .HasMaxLength(100) // same length as agent_capabilities.name
      .IsRequired();

    // Decision A36: smallint, 0 submitted, 1 working, 2 completed, 3 failed.
    builder.Property(task => task.Status)
      .HasConversion<short>()
      .HasColumnType("smallint");

    builder.Property(task => task.Input)
      .HasColumnType("jsonb")
      .IsRequired();
    builder.Property(task => task.Output)
      .HasColumnType("jsonb");

    builder.Property(task => task.AncestorChain)
      .HasColumnType("jsonb")
      .HasConversion(
        new ValueConverter<List<Guid>, string>(chain => SerializeChain(chain), json => DeserializeChain(json)),
        new ValueComparer<List<Guid>>(
          (left, right) => left!.SequenceEqual(right!),
          chain => chain.Aggregate(0, (hash, id) => HashCode.Combine(hash, id)),
          chain => chain.ToList()))
      .IsRequired();

    builder.Property(task => task.Error)
      .HasColumnType("jsonb")
      .HasConversion(new ValueConverter<TaskError, string>(error => SerializeError(error), json => DeserializeError(json)));

    builder.HasOne<Agent>().WithMany().HasForeignKey(task => task.CallerAgentId).OnDelete(DeleteBehavior.Restrict);
    builder.HasOne<Agent>().WithMany().HasForeignKey(task => task.TargetAgentId).OnDelete(DeleteBehavior.Restrict);
    builder.HasOne<AgentTask>().WithMany().HasForeignKey(task => task.ParentTaskId).OnDelete(DeleteBehavior.Restrict);

    // (status, deadline_at) serves the Week 7 timeout worker.
    builder.HasIndex(task => new { task.Status, task.DeadlineAt });
    builder.HasIndex(task => task.CallerAgentId);
    builder.HasIndex(task => task.TargetAgentId);
    builder.HasIndex(task => task.RootTaskId);
    builder.HasIndex(task => task.ParentTaskId);
  }
}

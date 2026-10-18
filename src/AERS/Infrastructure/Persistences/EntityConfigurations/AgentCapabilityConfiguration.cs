using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistences.Configurations;

public sealed class AgentCapabilityConfiguration : IEntityTypeConfiguration<AgentCapability>
{
  public void Configure(EntityTypeBuilder<AgentCapability> builder)
  {
    builder.ToTable("agent_capabilities");
    builder.HasKey(agentCapability => agentCapability.Id);
    builder.Property(x => x.Name)
      .HasMaxLength(100)
      .IsRequired();
    builder.Property(x => x.Description)
      .IsRequired();
    builder.Property(x => x.InputSchemaJson)
      .HasColumnName("input_schema")
      .HasColumnType("jsonb")
      .IsRequired();
    builder.Property(x => x.OutputSchemaJson)
      .HasColumnName("output_schema")
      .HasColumnType("jsonb")
      .IsRequired();
    builder.HasIndex(x => new { x.AgentId, x.Name }).IsUnique();
    builder.HasIndex(x => x.Name);
  }
}

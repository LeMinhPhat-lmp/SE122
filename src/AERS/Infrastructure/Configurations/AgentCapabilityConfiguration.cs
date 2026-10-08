using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Configurations;

public sealed class AgentCapabilityConfiguration : IEntityTypeConfiguration<AgentCapability>
{
  public void Configure(EntityTypeBuilder<AgentCapability> builder)
  {
    builder.HasKey(agentCapability => agentCapability.Id);
    builder.Property(x => x.Name)
      .HasMaxLength(100)
      .IsRequired();
    builder.Property(x => x.Description)
      .IsRequired();
    builder.Property(x => x.InputSchemaJson)
      .IsRequired();
    builder.Property(x => x.OutputSchemaJson)
      .IsRequired();
  }
}
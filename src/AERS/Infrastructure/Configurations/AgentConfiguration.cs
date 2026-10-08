using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Configurations;

public sealed class AgentConfiguration : IEntityTypeConfiguration<Agent>
{
  public void Configure(EntityTypeBuilder<Agent> builder)
  {
    builder.HasKey(agent => agent.Id);
    builder.Property(x => x.Name)
      .HasMaxLength(200)
      .IsRequired();
    builder.Property(x => x.Domain)
      .HasMaxLength(64)
      .IsRequired();
    builder.Property(x => x.Description)
      .IsRequired();
    builder.Property(x => x.OwnerRef)
      .HasMaxLength(256);
    builder.HasMany(x => x.Capabilities)
      .WithOne(x => x.Agent)
      .HasForeignKey(x => x.AgentId);
    builder.HasOne(agent => agent.Credential)
      .WithOne(credential => credential.Agent)
      .HasForeignKey<AgentCredential>(credential => credential.AgentId);
  }
}
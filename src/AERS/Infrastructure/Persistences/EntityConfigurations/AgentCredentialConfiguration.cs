using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistences.Configurations;

public sealed class AgentCredentialConfiguration : IEntityTypeConfiguration<AgentCredential>
{
  public void Configure(EntityTypeBuilder<AgentCredential> builder)
  {
    builder.ToTable("agent_credentials");
    builder.HasKey(agentCredential => agentCredential.Id);
    builder.Property(x => x.ApiKeyHash)
      .HasMaxLength(64) // SHA-256 hex
      .IsRequired();
    builder.HasIndex(x => x.ApiKeyHash)
      .IsUnique();
  }
}

using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Configurations;

public sealed class AgentCredentialConfiguration : IEntityTypeConfiguration<AgentCredential>
{
  public void Configure(EntityTypeBuilder<AgentCredential> builder)
  {
    builder.HasKey(agentCredential => agentCredential.Id);
    builder.Property(x => x.ApiKeyHash)
      .HasMaxLength(64)
      .IsRequired();
    builder.HasIndex(x => x.ApiKeyHash)
      .IsUnique();
  }
}
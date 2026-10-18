using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistences.Configurations;

public sealed class AgentConfiguration : IEntityTypeConfiguration<Agent>
{
  public void Configure(EntityTypeBuilder<Agent> builder)
  {
    builder.ToTable("agents");
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
    // Column names follow the ERD: registered_at / deregistered_at (soft delete)
    builder.Property(x => x.CreatedAt).HasColumnName("registered_at");
    builder.Property(x => x.DeletedAt).HasColumnName("deregistered_at");
    builder.HasIndex(x => x.Domain);
    builder.HasMany(x => x.Capabilities)
      .WithOne(x => x.Agent)
      .HasForeignKey(x => x.AgentId);
    builder.HasMany(x => x.Credentials)
      .WithOne(x => x.Agent)
      .HasForeignKey(x => x.AgentId);
  }
}

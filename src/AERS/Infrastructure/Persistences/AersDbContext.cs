using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistences;

public class AersDbContext(DbContextOptions<AersDbContext> options) : DbContext(options)
{
  public DbSet<Agent> Agents { get; set; }
  public DbSet<AgentCapability> AgentCapabilities { get; set; }
  public DbSet<AgentCredential> AgentCredentials { get; set; }


  protected override void OnModelCreating(ModelBuilder modelBuilder)
  {
    base.OnModelCreating(modelBuilder);

    modelBuilder.Entity<Agent>(entity =>
    {
      entity.HasKey(agent => agent.Id);
      entity.Property(x => x.Name)
            .HasMaxLength(200)
            .IsRequired();
      entity.Property(x => x.Domain)
            .HasMaxLength(64)
            .IsRequired();
      entity.Property(x => x.Description)
            .IsRequired();
      entity.Property(x => x.OwnerRef)
            .HasMaxLength(256);
      entity.HasMany(x => x.Capabilities)
            .WithOne(x => x.Agent)
            .HasForeignKey(x => x.AgentId);
      entity.HasOne(agent => agent.Credential)
            .WithOne(credential => credential.Agent)
            .HasForeignKey<AgentCredential>(credential => credential.AgentId);
    });
    modelBuilder.Entity<AgentCapability>(entity =>
    {
      entity.HasKey(agentCapability => agentCapability.Id);
      entity.Property(x => x.Name)
            .HasMaxLength(100)
            .IsRequired();

      entity.Property(x => x.Description)
            .IsRequired();

      entity.Property(x => x.InputSchemaJson)
            .IsRequired();

      entity.Property(x => x.OutputSchemaJson)
            .IsRequired();
    });
    modelBuilder.Entity<AgentCredential>(entity =>
    {
      entity.HasKey(agentCredential => agentCredential.Id);
      entity.Property(x => x.ApiKeyHash)
            .HasMaxLength(64)
            .IsRequired();

      entity.HasIndex(x => x.ApiKeyHash)
            .IsUnique();
    });
  }
}
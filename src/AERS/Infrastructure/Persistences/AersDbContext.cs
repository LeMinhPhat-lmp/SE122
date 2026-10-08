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
    modelBuilder.ApplyConfigurationsFromAssembly(typeof(AersDbContext).Assembly);
  }
}
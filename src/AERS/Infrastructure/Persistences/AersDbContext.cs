using System.Text.RegularExpressions;
using Domain.Abstractions;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistences;

public class AersDbContext(DbContextOptions<AersDbContext> options) : DbContext(options)
{
  public DbSet<Agent> Agents { get; set; }
  public DbSet<AgentCapability> AgentCapabilities { get; set; }
  public DbSet<AgentCredential> AgentCredentials { get; set; }
  public DbSet<AuditEvent> AuditEvents { get; set; }

  protected override void OnModelCreating(ModelBuilder modelBuilder)
  {
    base.OnModelCreating(modelBuilder);

    // Snake_case column names as in the ERD (design diagram D6). This runs before ApplyConfigurations
    // so that explicit column names in the entity configurations take precedence.
    foreach (var entityType in modelBuilder.Model.GetEntityTypes())
      foreach (var property in entityType.GetProperties())
        property.SetColumnName(Regex.Replace(property.Name, "(?<=[a-z0-9])([A-Z])", "_$1").ToLowerInvariant());

    modelBuilder.ApplyConfigurationsFromAssembly(typeof(AersDbContext).Assembly);
  }

  public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
  {
    ApplyAuditTimestamps();
    return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
  }

  public override int SaveChanges(bool acceptAllChangesOnSuccess)
  {
    ApplyAuditTimestamps();
    return base.SaveChanges(acceptAllChangesOnSuccess);
  }

  // Shared by the sync and async SaveChanges overrides.
  private void ApplyAuditTimestamps()
  {
    var now = DateTimeOffset.UtcNow;

    foreach (var entry in ChangeTracker.Entries<IAuditable>())
    {
      if (entry.State == EntityState.Added)
        entry.Property(nameof(IAuditable.CreatedAt)).CurrentValue = now;
      else if (entry.State == EntityState.Modified)
        entry.Property(nameof(IAuditable.UpdatedAt)).CurrentValue = now;
    }
  }
}

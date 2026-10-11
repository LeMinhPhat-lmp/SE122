using Domain.Abstractions;

namespace Domain.Entities;

public sealed class Agent : BaseEntity, IAuditable, ISoftDeletable
{
  public string Name { get; set; } = null!;
  public string Domain { get; set; } = null!;
  public string Description { get; set; } = null!;
  public string? OwnerRef { get; set; }
  public DateTime RegisteredAt { get; set; }
  public DateTime? DeregisteredAt { get; set; }
  public ICollection<AgentCapability> Capabilities { get; set; } = new List<AgentCapability>();
  public Guid CredentialId { get; set; }
  public AgentCredential Credential { get; set; } = null!;

  public bool IsDeleted => DeletedAt.HasValue;

  public DateTimeOffset? DeletedAt { get; set; }

  public DateTimeOffset CreatedAt { get; set; }

  public DateTimeOffset? UpdatedAt { get; set; }
}
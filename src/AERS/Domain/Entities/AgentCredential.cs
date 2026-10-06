using Domain.Abstractions;

namespace Domain.Entities;

public sealed class AgentCredential : BaseEntity, IAuditable
{
  public Guid AgentId { get; set; }
  public Agent Agent { get; set; } = null!;
  public string ApiKeyHash { get; set; } = null!;
  public DateTimeOffset CreatedAt { get; set; }
  public DateTimeOffset? UpdatedAt { get; set; }
  public DateTimeOffset? RevokedAt { get; set; }
  public bool IsRevoked => RevokedAt is not null && RevokedAt.HasValue;
}
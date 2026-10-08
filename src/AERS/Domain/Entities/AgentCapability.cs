using System.Text.Json;
using Domain.Abstractions;

namespace Domain.Entities;

public sealed class AgentCapability : BaseEntity, IAuditable
{
  public Guid AgentId { get; set; }
  public Agent Agent { get; set; } = null!;
  public string Name { get; set; } = null!;
  public string Description { get; set; } = null!;
  public JsonElement InputSchemaJson { get; set; }
  public JsonElement OutputSchemaJson { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
  public DateTimeOffset? UpdatedAt { get; set; }
}
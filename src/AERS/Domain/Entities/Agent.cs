using System.Text.Json;
using Domain.Abstractions;

namespace Domain.Entities;

public sealed class Agent : BaseEntity, IAuditable, ISoftDeletable
{
  public string Name { get; set; } = null!;
  public string Domain { get; set; } = null!;
  public string Description { get; set; } = null!;
  public string? OwnerRef { get; set; }
  public ICollection<AgentCapability> Capabilities { get; set; } = new List<AgentCapability>();
  public ICollection<AgentCredential> Credentials { get; set; } = new List<AgentCredential>();

  // CreatedAt is stored as "registered_at" and DeletedAt as "deregistered_at" (soft delete).
  public DateTimeOffset CreatedAt { get; set; }
  public DateTimeOffset? UpdatedAt { get; set; }
  public DateTimeOffset? DeletedAt { get; set; }
  public bool IsDeleted => DeletedAt.HasValue;

  public static Agent Register(Guid id, string name, string domain, string description, string? ownerRef) => new()
  {
    Id = id,
    Name = name,
    Domain = domain,
    Description = description,
    OwnerRef = ownerRef
  };

  public void AddCapability(string name, string description, JsonElement inputSchema, JsonElement outputSchema)
  {
    Capabilities.Add(new AgentCapability
    {
      AgentId = Id,
      Agent = this,
      Name = name,
      Description = description,
      InputSchemaJson = inputSchema,
      OutputSchemaJson = outputSchema
    });
  }

  public void AddCredential(string apiKeyHash)
  {
    Credentials.Add(new AgentCredential { AgentId = Id, Agent = this, ApiKeyHash = apiKeyHash });
  }
}

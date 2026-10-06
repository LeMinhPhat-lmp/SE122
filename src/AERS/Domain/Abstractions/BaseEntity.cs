namespace Domain.Abstractions;

public abstract class BaseEntity : IEntity
{
  public Guid Id { get; protected set; }
}
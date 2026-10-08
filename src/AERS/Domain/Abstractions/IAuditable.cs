namespace Domain.Abstractions;

public interface IAuditable
{
  DateTimeOffset CreatedAt { get; }
  DateTimeOffset? UpdatedAt { get; }
}
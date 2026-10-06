namespace Domain.Abstractions;

public interface ISoftDeletable
{
  bool IsDeleted { get; }
  DateTimeOffset DeletedAt { get; }
}
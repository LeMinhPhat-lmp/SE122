namespace Application.Interfaces.Runtime;

public interface IDependencyHealthChecker
{
  /// <summary>Returns true when both PostgreSQL and Redis are reachable.</summary>
  Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default);
}

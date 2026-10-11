namespace Application.Interfaces.Repositories;

public interface IGenericRespository<T>
{
  Task<T?> GetByIdAsync(Guid entityId, bool trackChanges = false, CancellationToken cancellationToken = default);
  Task<List<T>> ListEntitiesAsync(int? skip, int? take, bool trackChanges = false, CancellationToken cancellationToken = default);
  Task AddAsync(T entity, CancellationToken cancellationToken = default);
  Task AddRangeAsync(IEnumerable<T> entities, CancellationToken cancellationToken = default);
  void Update(T entity);
  void Remove(T entity);
  void RemoveRange(IEnumerable<T> entities);
}
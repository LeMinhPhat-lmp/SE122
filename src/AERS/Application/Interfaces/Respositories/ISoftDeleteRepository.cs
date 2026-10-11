namespace Application.Interfaces.Repositories;

public interface ISoftDeleteRepository<T>
{
  void Delete(T entity);
  void DeleteRange(IEnumerable<T> entities);
}
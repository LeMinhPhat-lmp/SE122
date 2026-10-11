using Domain.Entities;

namespace Application.Interfaces.Repositories;

public interface IAgentRepository : IGenericRespository<Agent>, ISoftDeleteRepository<Agent> { }
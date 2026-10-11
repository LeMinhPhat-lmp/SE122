using Domain.Entities;

namespace Application.Interfaces.Repositories;

public interface IAgentCapabilityRepository : IGenericRespository<AgentCapability>, ISoftDeleteRepository<AgentCapability> { }
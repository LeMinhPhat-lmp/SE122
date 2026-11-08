using Application.DTOs.Tasks;
using Application.Models;

namespace Application.Interfaces.Features;

/// <summary>
/// The whole hub logic, testable without SignalR. The hub is a thin adapter that calls this method (decision A22).
/// It never throws for business or unexpected errors: they become an EnvelopeAck (A38).
/// </summary>
public interface ITaskMessageService
{
  Task<EnvelopeAck> HandleAsync(TaskEnvelope envelope, AuthenticatedAgentContext sender, CancellationToken cancellationToken = default);
}

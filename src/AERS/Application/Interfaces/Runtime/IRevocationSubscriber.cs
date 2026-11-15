namespace Application.Interfaces.Runtime;

/// <summary>Receiving side of the revocation channel (ADR-W8-03). Every instance subscribes once; messages that are not an agent id are ignored.</summary>
public interface IRevocationSubscriber
{
  Task SubscribeAsync(Action<Guid> onRevoked, CancellationToken cancellationToken = default);

  Task UnsubscribeAsync(CancellationToken cancellationToken = default);
}

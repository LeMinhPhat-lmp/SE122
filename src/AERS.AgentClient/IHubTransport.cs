using System.Text.Json;

namespace AERS.AgentClient;

public interface IHubTransport : IAsyncDisposable
{
  bool IsConnected { get; }
  event Func<JsonElement, Task>? MessageReceived;
  event Func<Exception?, Task>? Closed;
  event Action? Reconnecting;
  event Action<string?>? Reconnected;
  Task StartAsync(CancellationToken cancellationToken = default);
  Task StopAsync(CancellationToken cancellationToken = default);
  Task<JsonElement> SendAsync(JsonElement envelope, CancellationToken cancellationToken = default);
}

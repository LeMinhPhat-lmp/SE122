using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace AERS.AgentClient;

public sealed class SignalRHubTransport : IHubTransport
{
  private readonly HubConnection connection;

  public SignalRHubTransport(AersAgentConnectionOptions options)
  {
    ArgumentNullException.ThrowIfNull(options);
    if (!options.HubUrl.IsAbsoluteUri)
      throw new ArgumentException("HubUrl must be an absolute URI.", nameof(options));

    connection = new HubConnectionBuilder()
      .WithUrl(options.HubUrl, builder =>
      {
        if (!string.IsNullOrWhiteSpace(options.ApiKey))
          builder.Headers["X-Api-Key"] = options.ApiKey;
      })
      .WithAutomaticReconnect(new[] { TimeSpan.Zero, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10) })
      .AddJsonProtocol()
      .Build();
    connection.On<JsonElement>("Receive", payload => MessageReceived?.Invoke(payload) ?? Task.CompletedTask);
    connection.Reconnecting += exception =>
    {
      Reconnecting?.Invoke();
      return Task.CompletedTask;
    };
    connection.Reconnected += connectionId =>
    {
      Reconnected?.Invoke(connectionId);
      return Task.CompletedTask;
    };
    connection.Closed += exception => Closed?.Invoke(exception) ?? Task.CompletedTask;
  }

  public bool IsConnected => connection.State == HubConnectionState.Connected;
  public event Func<JsonElement, Task>? MessageReceived;
  public event Func<Exception?, Task>? Closed;
  public event Action? Reconnecting;
  public event Action<string?>? Reconnected;

  public Task StartAsync(CancellationToken cancellationToken = default) => connection.StartAsync(cancellationToken);
  public Task StopAsync(CancellationToken cancellationToken = default) => connection.StopAsync(cancellationToken);
  public Task<JsonElement> SendAsync(JsonElement envelope, CancellationToken cancellationToken = default) =>
    connection.InvokeAsync<JsonElement>("Send", envelope, cancellationToken);
  public ValueTask DisposeAsync() => connection.DisposeAsync();
}

using System.Globalization;
using Application.Interfaces.Runtime;
using Application.Mappings;
using Application.Models;
using Infrastructure.Options;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Infrastructure.Runtime;

/// <summary>
/// Hash {prefix}task:{taskId} with taskId, status, updatedAt, connectionId, instanceId, deadlineAt (A43, A44, ADR-W7-01).
/// SetAsync is one Lua script (HSET, then PEXPIRE) instead of an IBatch: a script is atomic on the server, so the hash can never
/// exist without a TTL even if the process dies right after sending it; a batch only pipelines two independent commands.
/// </summary>
public sealed class RedisTaskRuntimeStateStore(IConnectionMultiplexer redis, IOptions<RedisOptions> options)
  : RedisStoreBase(redis, options), ITaskRuntimeStateStore
{
  private const string SetScript = "redis.call('HSET', KEYS[1], unpack(ARGV, 2)) redis.call('PEXPIRE', KEYS[1], ARGV[1]) return 1";

  private string Key(Guid taskId) => $"{KeyPrefix}task:{taskId}";

  private static string Format(DateTimeOffset value) => value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);

  public Task SetAsync(TaskRuntimeState state, TimeSpan ttl, CancellationToken cancellationToken = default) =>
    ExecuteAsync(database => database.ScriptEvaluateAsync(
      SetScript,
      new RedisKey[] { Key(state.TaskId) },
      new RedisValue[]
      {
        (long)Math.Max(1, Math.Ceiling(ttl.TotalMilliseconds)),
        "taskId", state.TaskId.ToString(),
        "status", state.Status.ToWire(),
        "updatedAt", Format(state.UpdatedAt),
        "connectionId", state.ConnectionId ?? string.Empty,
        "instanceId", state.InstanceId ?? string.Empty,
        "deadlineAt", state.DeadlineAt is { } deadline ? Format(deadline) : string.Empty
      }), cancellationToken);

  public Task<TaskRuntimeState?> GetAsync(Guid taskId, CancellationToken cancellationToken = default) =>
    ExecuteAsync<TaskRuntimeState?>(async database =>
    {
      var entries = await database.HashGetAllAsync(Key(taskId));
      if (entries.Length == 0)
        return null;

      var fields = entries.ToDictionary(entry => entry.Name.ToString(), entry => entry.Value.ToString());
      return new TaskRuntimeState(
        taskId,
        TaskMappings.FromWire(fields["status"]),
        DateTimeOffset.Parse(fields["updatedAt"], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
        fields["connectionId"].Length == 0 ? null : fields["connectionId"],
        fields["instanceId"].Length == 0 ? null : fields["instanceId"],
        fields["deadlineAt"].Length == 0 ? null : DateTimeOffset.Parse(fields["deadlineAt"], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
    }, cancellationToken);
}

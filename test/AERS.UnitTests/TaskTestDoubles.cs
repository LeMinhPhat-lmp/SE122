using System.Text.Json;
using Application.DTOs.Result.Responses;
using Application.DTOs.Tasks;
using Application.Exceptions;
using Application.Interfaces;
using Application.Interfaces.Features;
using Application.Interfaces.Policies;
using Application.Interfaces.Realtime;
using Application.Interfaces.Repositories;
using Application.Interfaces.Runtime;
using Application.Interfaces.Validation;
using Application.Models;
using Application.Options;
using Application.Policies;
using Application.Services;
using Domain.Entities;
using Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace AERS.UnitTests;

internal sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
{
  private DateTimeOffset now = start;

  public override DateTimeOffset GetUtcNow() => now;

  public void Advance(TimeSpan by) => now += by;
}

internal sealed class CapturingLogger<T> : ILogger<T>
{
  public List<(LogLevel Level, string Message)> Entries { get; } = [];

  public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

  public bool IsEnabled(LogLevel logLevel) => true;

  public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
    Entries.Add((logLevel, formatter(state, exception)));

  public IEnumerable<string> Warnings => Entries.Where(entry => entry.Level == LogLevel.Warning).Select(entry => entry.Message);
}

internal sealed class InMemoryAgentRepository : IAgentRepository
{
  public List<Agent> Agents { get; } = [];
  public int CapabilityLookups { get; private set; }

  public Task<Agent?> GetByIdAsync(Guid entityId, bool trackChanges = false, CancellationToken cancellationToken = default) =>
    Task.FromResult(Agents.FirstOrDefault(agent => agent.Id == entityId && !agent.IsDeleted));

  public Task<Agent?> GetWithCapabilitiesAsync(Guid agentId, CancellationToken cancellationToken = default)
  {
    CapabilityLookups++;
    return Task.FromResult(Agents.FirstOrDefault(agent => agent.Id == agentId && !agent.IsDeleted));
  }

  public void Delete(Agent entity) => entity.DeletedAt = TestData.BaseTime;

  public Task<AgentPage> ListActiveAsync(string domain, string? capabilityName, DiscoveryCursorPosition? after, int limit, CancellationToken cancellationToken = default) => throw new NotSupportedException();
  public Task<List<Agent>> ListEntitiesAsync(int? skip, int? take, bool trackChanges = false, CancellationToken cancellationToken = default) => throw new NotSupportedException();
  public Task AddAsync(Agent entity, CancellationToken cancellationToken = default) => throw new NotSupportedException();
  public Task AddRangeAsync(IEnumerable<Agent> entities, CancellationToken cancellationToken = default) => throw new NotSupportedException();
  public void Update(Agent entity) => throw new NotSupportedException();
  public void Remove(Agent entity) => throw new NotSupportedException();
  public void RemoveRange(IEnumerable<Agent> entities) => throw new NotSupportedException();
  public void DeleteRange(IEnumerable<Agent> entities) => throw new NotSupportedException();
}

internal sealed class InMemoryTaskRepository : ITaskRepository
{
  private readonly object gate = new();

  public Dictionary<Guid, AgentTask> Tasks { get; } = new();
  public List<TaskStatusHistory> History { get; } = [];

  /// <summary>Makes the next compare-and-set lose, as if another instance had changed the task first.</summary>
  public bool FailNextTransition { get; set; }

  public Task<AgentTask?> GetByIdAsync(Guid taskId, bool trackChanges = false, CancellationToken cancellationToken = default) =>
    Task.FromResult<AgentTask?>(Tasks.GetValueOrDefault(taskId));

  public Task AddAsync(AgentTask task, CancellationToken cancellationToken = default)
  {
    Tasks[task.Id] = task;
    return Task.CompletedTask;
  }

  public Task AddHistoryAsync(TaskStatusHistory entry, CancellationToken cancellationToken = default)
  {
    History.Add(entry);
    return Task.CompletedTask;
  }

  public Task<bool> TryTransitionAsync(Guid taskId, AgentTaskStatus expected, AgentTaskStatus next, DateTimeOffset now, JsonElement? output, TaskError? error, CancellationToken cancellationToken = default)
  {
    lock (gate)
    {
      if (FailNextTransition)
      {
        FailNextTransition = false;
        return Task.FromResult(false);
      }

      if (!Tasks.TryGetValue(taskId, out var task) || task.Status != expected)
        return Task.FromResult(false);

      task.Status = next;
      if (next == AgentTaskStatus.Working)
        task.StartedAt = now;
      if (next.IsTerminal())
      {
        task.CompletedAt = now;
        task.Output = output;
        task.Error = error;
      }

      return Task.FromResult(true);
    }
  }

  public Task<IReadOnlyList<ExpiredTaskRef>> ListExpiredAsync(DateTimeOffset now, int limit, CancellationToken cancellationToken = default) =>
    Task.FromResult<IReadOnlyList<ExpiredTaskRef>>(Tasks.Values
      .Where(task => !task.Status.IsTerminal() && task.DeadlineAt < now)
      .OrderBy(task => task.DeadlineAt).ThenBy(task => task.Id)
      .Select(task => new ExpiredTaskRef(task.Id, task.Status))
      .Take(limit).ToList());

  public Task<IReadOnlyList<WorkingTaskRef>> ListWorkingByTargetAsync(Guid targetAgentId, int limit, CancellationToken cancellationToken = default) =>
    Task.FromResult<IReadOnlyList<WorkingTaskRef>>(Tasks.Values
      .Where(task => task.TargetAgentId == targetAgentId && task.Status == AgentTaskStatus.Working)
      .OrderBy(task => task.CreatedAt).ThenBy(task => task.Id)
      .Select(task => new WorkingTaskRef(task.Id, task.CallerAgentId, task.DeadlineAt))
      .Take(limit).ToList());

  public Task<IReadOnlyList<AgentTask>> ListUnacknowledgedTerminalAsync(Guid callerAgentId, int limit, CancellationToken cancellationToken = default) =>
    Task.FromResult<IReadOnlyList<AgentTask>>(Tasks.Values
      .Where(task => task.CallerAgentId == callerAgentId && task.Status.IsTerminal() && task.AcknowledgedAt is null)
      .OrderBy(task => task.CompletedAt).ThenBy(task => task.Id)
      .Take(limit).ToList());

  public Task<bool> TryAcknowledgeAsync(Guid taskId, Guid callerAgentId, DateTimeOffset now, CancellationToken cancellationToken = default)
  {
    if (!Tasks.TryGetValue(taskId, out var task) || task.CallerAgentId != callerAgentId || task.AcknowledgedAt is not null)
      return Task.FromResult(false);

    task.AcknowledgedAt = now;
    return Task.FromResult(true);
  }

  public Task<int> CountWorkingAsTargetAsync(Guid agentId, CancellationToken cancellationToken = default) =>
    Task.FromResult(Tasks.Values.Any(task => task.TargetAgentId == agentId && task.Status == AgentTaskStatus.Working) ? 1 : 0);
}

internal sealed class InMemoryAuditRepository : IAuditEventRepository
{
  public List<AuditEvent> Events { get; } = [];

  public Task AddAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default)
  {
    Events.Add(auditEvent);
    return Task.CompletedTask;
  }
}

internal sealed class InMemoryCredentialRepository : IAgentCredentialRepository
{
  public Task<List<AgentCredential>> ListByAgentIdAsync(Guid agentId, bool trackChanges = false, CancellationToken cancellationToken = default) =>
    Task.FromResult(new List<AgentCredential>());

  public Task<AgentCredential?> GetActiveByHashAsync(string apiKeyHash, CancellationToken cancellationToken = default) => throw new NotSupportedException();
  public Task<AgentCredential?> GetByIdAsync(Guid entityId, bool trackChanges = false, CancellationToken cancellationToken = default) => throw new NotSupportedException();
  public Task<List<AgentCredential>> ListEntitiesAsync(int? skip, int? take, bool trackChanges = false, CancellationToken cancellationToken = default) => throw new NotSupportedException();
  public Task AddAsync(AgentCredential entity, CancellationToken cancellationToken = default) => throw new NotSupportedException();
  public Task AddRangeAsync(IEnumerable<AgentCredential> entities, CancellationToken cancellationToken = default) => throw new NotSupportedException();
  public void Update(AgentCredential entity) => throw new NotSupportedException();
  public void Remove(AgentCredential entity) => throw new NotSupportedException();
  public void RemoveRange(IEnumerable<AgentCredential> entities) => throw new NotSupportedException();
}

internal sealed class InMemoryTransaction : ITransaction
{
  public Task CommitTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
  public Task RollbackTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
  public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class InMemoryUnitOfWork(
  InMemoryAgentRepository agents,
  InMemoryTaskRepository tasks,
  InMemoryAuditRepository audit,
  InMemoryCredentialRepository credentials) : IUnitOfWork
{
  public IAgentRepository AgentRepository => agents;
  public ITaskRepository TaskRepository => tasks;
  public IAuditEventRepository AuditEventRepository => audit;
  public IAgentCredentialRepository AgentCredentialRepository => credentials;
  public IAgentCapabilityRepository AgentCapabilityRepository => throw new NotSupportedException();

  public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
  public Task<ITransaction> StartTransaction(CancellationToken cancellationToken = default) => Task.FromResult<ITransaction>(new InMemoryTransaction());
}

internal sealed class FakeRateLimiter : IRateLimiter
{
  public int Calls { get; private set; }
  public Exception? FailWith { get; set; }
  public Func<RateLimitDecision>? Decide { get; set; }

  public Task<RateLimitDecision> TryAcquireAsync(Guid callerAgentId, CancellationToken cancellationToken = default)
  {
    Calls++;
    if (FailWith is not null)
      throw FailWith;

    return Task.FromResult(Decide?.Invoke() ?? new RateLimitDecision(true, 100, 99, TestData.BaseTime.AddMinutes(1)));
  }
}

internal sealed class FakeRuntimeStateStore : ITaskRuntimeStateStore
{
  public List<(TaskRuntimeState State, TimeSpan Ttl, AgentTaskStatus? StoredStatus)> Writes { get; } = [];
  public Exception? FailWith { get; set; }
  public Func<Guid, AgentTaskStatus?>? StoredStatusOf { get; set; }

  public Task SetAsync(TaskRuntimeState state, TimeSpan ttl, CancellationToken cancellationToken = default)
  {
    if (FailWith is not null)
      throw FailWith;

    Writes.Add((state, ttl, StoredStatusOf?.Invoke(state.TaskId)));
    return Task.CompletedTask;
  }

  public Task<TaskRuntimeState?> GetAsync(Guid taskId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

internal sealed class FakeConnectionRegistry : IConnectionRegistry
{
  public Dictionary<Guid, ConnectionInfo> Connections { get; } = new();
  public bool FailOnResolve { get; set; }

  public Task BindAsync(Guid agentId, string connectionId, string instanceId, CancellationToken cancellationToken = default)
  {
    Connections[agentId] = new ConnectionInfo(connectionId, instanceId);
    return Task.CompletedTask;
  }

  public Task<bool> UnbindAsync(Guid agentId, string connectionId, CancellationToken cancellationToken = default)
  {
    if (Connections.TryGetValue(agentId, out var current) && current.ConnectionId == connectionId)
    {
      Connections.Remove(agentId);
      return Task.FromResult(true);
    }

    return Task.FromResult(false);
  }

  public Task<ConnectionInfo?> ResolveAsync(Guid agentId, CancellationToken cancellationToken = default)
  {
    if (FailOnResolve)
      throw new StateStoreUnavailableException(new InvalidOperationException("redis down"));

    return Task.FromResult<ConnectionInfo?>(Connections.GetValueOrDefault(agentId));
  }

  public Task PurgeInstanceAsync(string instanceId, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

internal sealed class FakeMessageRelay : IMessageRelay
{
  public List<(ConnectionInfo Target, TaskEnvelope Envelope)> Sent { get; } = [];
  public Exception? FailWith { get; set; }

  public Task SendAsync(ConnectionInfo target, TaskEnvelope envelope, CancellationToken cancellationToken = default)
  {
    if (FailWith is not null)
      throw FailWith;

    Sent.Add((target, envelope));
    return Task.CompletedTask;
  }
}

internal sealed class FakeNotifier : ITaskNotifier
{
  public List<(Guid CallerId, TaskResult Result)> Results { get; } = [];
  public List<(Guid CallerId, Guid TaskId)> Working { get; } = [];

  public Task NotifyResultAsync(Guid callerAgentId, TaskResult result, CancellationToken cancellationToken = default)
  {
    Results.Add((callerAgentId, result));
    return Task.CompletedTask;
  }

  public Task NotifyWorkingAsync(Guid callerAgentId, Guid taskId, CancellationToken cancellationToken = default)
  {
    Working.Add((callerAgentId, taskId));
    return Task.CompletedTask;
  }
}

internal sealed class FakeDisconnectTracker : IDisconnectTracker
{
  public Dictionary<Guid, DateTimeOffset> Marks { get; } = new();
  public Exception? FailWith { get; set; }

  public Task MarkDisconnectedAsync(Guid agentId, DateTimeOffset at, CancellationToken cancellationToken = default)
  {
    Marks[agentId] = at;
    return Task.CompletedTask;
  }

  public Task ClearAsync(Guid agentId, CancellationToken cancellationToken = default)
  {
    Marks.Remove(agentId);
    return Task.CompletedTask;
  }

  public Task<IReadOnlyList<Guid>> ListDueAsync(DateTimeOffset notAfter, int limit, CancellationToken cancellationToken = default)
  {
    if (FailWith is not null)
      throw FailWith;

    return Task.FromResult<IReadOnlyList<Guid>>(Marks.Where(mark => mark.Value <= notAfter).OrderBy(mark => mark.Value).Select(mark => mark.Key).Take(limit).ToList());
  }
}

internal sealed class FakeSchemaValidator : IInputSchemaValidator
{
  public List<ErrorDetail> Violations { get; } = [];
  public Exception? FailWith { get; set; }

  public IReadOnlyList<ErrorDetail> Validate(JsonElement schema, JsonElement instance)
  {
    if (FailWith is not null)
      throw FailWith;

    return Violations.ToList();
  }
}

internal sealed class RecordingPresenceStore : IAgentPresenceStore
{
  public List<Guid> Removed { get; } = [];

  public Task RemoveAsync(Guid agentId, CancellationToken cancellationToken = default)
  {
    Removed.Add(agentId);
    return Task.CompletedTask;
  }

  public Task MarkAliveAsync(Guid agentId, DateTimeOffset now, TimeSpan aliveTtl, CancellationToken cancellationToken = default) => Task.CompletedTask;
  public Task<AgentPresence> GetAsync(Guid agentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
  public Task<IReadOnlyDictionary<Guid, bool>> GetOnlineStatusAsync(IReadOnlyCollection<Guid> agentIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

/// <summary>Everything a task test needs, wired like production but with in-memory fakes.</summary>
internal sealed class TaskFixture
{
  public InMemoryAgentRepository Agents { get; } = new();
  public InMemoryTaskRepository Tasks { get; } = new();
  public InMemoryAuditRepository Audit { get; } = new();
  public InMemoryUnitOfWork UnitOfWork { get; }
  public FakeRateLimiter Limiter { get; } = new();
  public FakeRuntimeStateStore State { get; } = new();
  public FakeConnectionRegistry Connections { get; } = new();
  public FakeMessageRelay Relay { get; } = new();
  public FakeSchemaValidator Validator { get; } = new();
  public ManualTimeProvider Time { get; } = new(TestData.BaseTime);
  public CapturingLogger<TaskService> Log { get; } = new();
  public AersPolicyOptions Policy { get; } = new();
  public TaskOptions TaskSettings { get; } = new();
  public TaskService Service { get; }

  public TaskFixture()
  {
    UnitOfWork = new InMemoryUnitOfWork(Agents, Tasks, Audit, new InMemoryCredentialRepository());
    State.StoredStatusOf = id => Tasks.Tasks.GetValueOrDefault(id)?.Status;
    Service = new TaskService(
      UnitOfWork,
      new TaskPolicyEvaluator(new FakeDomainAccessPolicy((caller, target) => caller == target), Validator, Microsoft.Extensions.Options.Options.Create(Policy)),
      Limiter,
      Validator,
      Connections,
      Relay,
      State,
      Microsoft.Extensions.Options.Options.Create(TaskSettings),
      Microsoft.Extensions.Options.Options.Create(Policy),
      Microsoft.Extensions.Options.Options.Create(new InstanceOptions { Id = "test-1" }),
      Time,
      Log);
  }

  public Agent AddAgent(string domain = "jobhunt", params string[] capabilities)
  {
    var agent = TestData.CreateAgent(domain, 0, capabilities.Length == 0 ? new[] { "echo" } : capabilities);
    Agents.Agents.Add(agent);
    return agent;
  }

  public static AuthenticatedAgentContext As(Agent agent) => TestData.AgentCaller(agent.Id, agent.Domain);

  public static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

  /// <summary>Creates a task through the service and, when asked, moves it to working like a successful dispatch would.</summary>
  public async Task<AgentTask> CreateAsync(Agent caller, Agent target, AgentTask? parent = null, bool working = true)
  {
    var result = await Service.CreateAsync(
      new CreateTaskRequest(target.Id.ToString(), "echo", Json("{}"), parent?.Id.ToString()), As(caller));
    Assert.True(result.IsSuccess, result.ErrorCode);

    var task = result.Data!;
    if (working)
      task.Status = AgentTaskStatus.Working;
    return task;
  }
}

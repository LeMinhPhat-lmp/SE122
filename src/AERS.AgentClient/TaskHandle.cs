using System.Text.Json;

namespace AERS.AgentClient;

public sealed class TaskHandle
{
  private readonly TaskCompletionSource<TaskOutcome> completion =
    new(TaskCreationOptions.RunContinuationsAsynchronously);
  private int workingRaised;

  internal TaskHandle(Guid taskId, string status)
  {
    TaskId = taskId;
    Status = status;
  }

  public Guid TaskId { get; }
  public string Status { get; internal set; }
  public Task<TaskOutcome> Completion => completion.Task;
  public event EventHandler? Working;

  public Task<TaskOutcome> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
  {
    if (timeout <= TimeSpan.Zero)
      throw new ArgumentOutOfRangeException(nameof(timeout));
    return WaitCoreAsync(timeout, cancellationToken);
  }

  internal void RaiseWorking()
  {
    Status = "working";
    if (Interlocked.Exchange(ref workingRaised, 1) == 0)
      Working?.Invoke(this, EventArgs.Empty);
  }

  internal bool TryComplete(TaskOutcome outcome)
  {
    Status = outcome.Status;
    return completion.TrySetResult(outcome);
  }

  private async Task<TaskOutcome> WaitCoreAsync(TimeSpan timeout, CancellationToken cancellationToken)
  {
    try
    {
      return await completion.Task.WaitAsync(timeout, cancellationToken);
    }
    catch (TimeoutException)
    {
      throw new TimeoutException($"Task {TaskId:D} did not complete within the requested timeout.");
    }
  }
}

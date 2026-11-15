using Microsoft.Extensions.Options;

namespace Application.Options;

/// <summary>Cross-validates Aers:Tasks against Aers:Relay (MaxInputBytes and MaxOutputBytes must fit in one message).</summary>
public sealed class TaskOptionsValidator(IOptions<RelayOptions> relayOptions) : IValidateOptions<TaskOptions>
{
  public ValidateOptionsResult Validate(string? name, TaskOptions options)
  {
    var failures = new List<string>();
    var maxMessageBytes = relayOptions.Value.MaxMessageBytes;

    if (options.DefaultTaskTimeoutSeconds < 1)
      failures.Add("Aers:Tasks:DefaultTaskTimeoutSeconds must be at least 1.");
    if (options.MaxInputBytes < 1 || options.MaxInputBytes > maxMessageBytes)
      failures.Add("Aers:Tasks:MaxInputBytes must be between 1 and Aers:Relay:MaxMessageBytes.");
    if (options.MaxOutputBytes < 1 || options.MaxOutputBytes > maxMessageBytes)
      failures.Add("Aers:Tasks:MaxOutputBytes must be between 1 and Aers:Relay:MaxMessageBytes.");
    if (options.TimeoutScanIntervalSeconds < 1)
      failures.Add("Aers:Tasks:TimeoutScanIntervalSeconds must be at least 1.");
    if (options.TimeoutBatchSize < 1)
      failures.Add("Aers:Tasks:TimeoutBatchSize must be at least 1.");
    if (options.RuntimeStateGraceSeconds < 1)
      failures.Add("Aers:Tasks:RuntimeStateGraceSeconds must be at least 1.");
    if (options.RuntimeStateTerminalTtlSeconds < 1)
      failures.Add("Aers:Tasks:RuntimeStateTerminalTtlSeconds must be at least 1.");
    if (options.RedeliveryBatchSize < 1)
      failures.Add("Aers:Tasks:RedeliveryBatchSize must be at least 1.");
    if (options.TargetDisconnectGraceSeconds < 1)
      failures.Add("Aers:Tasks:TargetDisconnectGraceSeconds must be at least 1.");

    return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
  }
}

namespace Application.Constants;

/// <summary>Values of TaskEnvelope.messageType (Protocol Spec section 6.2, decision A23).</summary>
public static class TaskMessageTypes
{
  public const string Create = "task.create";
  public const string Invoke = "task.invoke";
  public const string Result = "task.result";
  public const string StatusEvent = "task.status_event";

  /// <summary>Message types a client may send to the hub.</summary>
  public static readonly IReadOnlySet<string> ClientToServer = new HashSet<string>(StringComparer.Ordinal) { Create, Result, StatusEvent };

  public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal) { Create, Invoke, Result, StatusEvent };
}

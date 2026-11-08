using System.Text.Json;

namespace Application.DTOs.Tasks;

/// <summary>Payload of task.invoke, AERS to target agent (Protocol Spec section 6.3). ancestorChain never appears here.</summary>
public sealed record TaskInvocation(
  string TaskId,
  string RootTaskId,
  string? ParentTaskId,
  int Depth,
  string Capability,
  JsonElement Input,
  PolicyContext PolicyContext);

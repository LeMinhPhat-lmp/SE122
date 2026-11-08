namespace Application.DTOs.Tasks;

/// <summary>Policy context delivered with task.invoke (Protocol Spec section 9.1). Contains no ancestorChain.</summary>
public sealed record PolicyContext(
  string CallerAgentId,
  string TargetAgentId,
  string Capability,
  string? CallerDomain,
  string? TargetDomain,
  string? RootTaskId,
  string? ParentTaskId,
  int? Depth,
  string? OwnerRef);

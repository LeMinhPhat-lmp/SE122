namespace Application.DTOs.Agent.Requests;

/// <summary>
/// Deregistration body must be empty or "{}" (Protocol Spec section 2.2). This type has no members
/// so strict JSON binding rejects any property that is sent.
/// </summary>
public sealed record DeregistrationRequest;

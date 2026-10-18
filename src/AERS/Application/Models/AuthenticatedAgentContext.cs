namespace Application.Models;

/// <summary>Identity of the caller after authentication. AgentId is null for registration-key callers.</summary>
public sealed record AuthenticatedAgentContext(Guid? AgentId, string? Domain, string? AuthenticationScheme);

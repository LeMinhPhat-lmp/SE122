namespace Application.Models;

/// <summary>Outcome of one rate limit check (A56). Remaining never goes below zero; ResetAt is the end of the current window (UTC).</summary>
public sealed record RateLimitDecision(bool Allowed, int Limit, int Remaining, DateTimeOffset ResetAt);

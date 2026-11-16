namespace Application.Models;

public sealed record RateLimitDecision(bool Allowed, int Limit, int Remaining, DateTimeOffset ResetAt);

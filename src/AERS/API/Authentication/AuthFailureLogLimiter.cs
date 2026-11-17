namespace API.Authentication;

/// <summary>
/// Keeps authentication failure logs usable under attack (ADR-W8-04): at most one log per (remote address, failure class) per
/// 10 seconds, in memory, at most 10,000 entries. Entries older than a minute are evicted when the table is full; if it is still
/// full the oldest entry is dropped. The values are constants on purpose (no configuration).
/// </summary>
public sealed class AuthFailureLogLimiter(TimeProvider timeProvider)
{
  public static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(10);
  public static readonly TimeSpan EntryLifetime = TimeSpan.FromMinutes(1);
  public const int MaxEntries = 10_000;

  private readonly Dictionary<(string Address, string Class), DateTimeOffset> lastLogged = new();
  private readonly object gate = new();

  public int Count
  {
    get
    {
      lock (gate)
        return lastLogged.Count;
    }
  }

  public bool ShouldLog(string remoteAddress, string failureClass)
  {
    var now = timeProvider.GetUtcNow();
    var key = (remoteAddress, failureClass);

    lock (gate)
    {
      if (lastLogged.TryGetValue(key, out var last) && now - last < MinInterval)
        return false;

      if (!lastLogged.ContainsKey(key) && lastLogged.Count >= MaxEntries)
      {
        foreach (var stale in lastLogged.Where(entry => now - entry.Value >= EntryLifetime).Select(entry => entry.Key).ToList())
          lastLogged.Remove(stale);

        if (lastLogged.Count >= MaxEntries)
          lastLogged.Remove(lastLogged.MinBy(entry => entry.Value).Key);
      }

      lastLogged[key] = now;
      return true;
    }
  }
}

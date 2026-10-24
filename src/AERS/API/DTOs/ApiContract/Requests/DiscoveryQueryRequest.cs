using Microsoft.AspNetCore.Mvc;

namespace API.DTOs.ApiContract.Requests;

/// <summary>
/// Query string of GET /agents. Only binds raw values; range, pattern and cursor checks happen in the Application layer
/// so that every violation is reported together. Explicit names keep the ModelState keys (and so the error paths)
/// identical to the query parameter names.
/// </summary>
public sealed class DiscoveryQueryRequest
{
  /// <summary>Domain to list. Defaults to the caller's own domain.</summary>
  [FromQuery(Name = "domain")] public string? Domain { get; init; }

  /// <summary>Exact, case-sensitive capability name filter.</summary>
  [FromQuery(Name = "capability")] public string? Capability { get; init; }

  /// <summary>When true, only agents that are currently online are returned. Default false.</summary>
  [FromQuery(Name = "onlineOnly")] public bool OnlineOnly { get; init; }

  /// <summary>Page size, 1 to the configured maximum (default 50, max 100).</summary>
  [FromQuery(Name = "limit")] public int? Limit { get; init; }

  /// <summary>Opaque cursor from the previous page's nextCursor.</summary>
  [FromQuery(Name = "cursor")] public string? Cursor { get; init; }
}

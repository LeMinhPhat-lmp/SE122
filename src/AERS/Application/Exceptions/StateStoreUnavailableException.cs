using Application.Constants;

namespace Application.Exceptions;

/// <summary>
/// The runtime state store (Redis) could not be reached. Callers where this is not fatal (for example registration,
/// which has already committed to the database) catch it; everywhere else it reaches the global handler as INTERNAL_ERROR.
/// </summary>
public sealed class StateStoreUnavailableException(Exception innerException)
  : ApplicationException(ErrorCodes.InternalError, "The runtime state store is unavailable.", innerException);

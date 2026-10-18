using API.Extensions;
using Application.DTOs.Result.Enums;
using Application.Interfaces.Errors;
using Microsoft.AspNetCore.Diagnostics;

namespace API.Exceptions;

/// <summary>
/// Last line of defense: every exception that no layer handled locally ends up here. The error classifier decides
/// what it means; this handler only logs and writes the response.
/// </summary>
public sealed class GlobalExceptionHandler(IErrorClassifier errorClassifier, ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
  public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
  {
    var failure = errorClassifier.Classify(exception);

    if (failure.ResultStatus == ResultStatus.InternalError)
      logger.LogError(exception, "Unhandled exception for {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);

    await httpContext.Response.WriteErrorAsync(failure, cancellationToken);

    return true;
  }
}

using System.Text.Json;
using API.DTOs.ApiContract.Responses;
using Microsoft.AspNetCore.Diagnostics;

namespace API.Excpetions;

public sealed class GlobalExceptionHandler : IExceptionHandler
{
  public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
  {
    var result = ApiResponse<object>.Failure(StatusCodes.Status500InternalServerError, "An unexpected error occured");

    httpContext.Response.ContentType = "application/json";
    httpContext.Response.StatusCode = (int)StatusCodes.Status500InternalServerError;
    await httpContext.Response.WriteAsync(JsonSerializer.Serialize(result));

    return true;
  }
}
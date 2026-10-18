using System.Text.Json;
using System.Text.Json.Serialization;
using API.Authentication;
using API.Exceptions;
using Application.Constants;
using Application.DTOs.Result.Enums;
using Application.DTOs.Result.Responses;
using Application.Interfaces.Errors;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;

namespace API.Extensions;

public static class DependencyInjection
{
  public static IServiceCollection AddApiServices(this IServiceCollection services)
  {
    services
      .AddControllers()
      .AddJsonOptions(options =>
      {
        // Strict binding replaces manual body parsing: unknown properties (for example "agentId" in a
        // registration payload), missing required members and null values for non-nullable members are rejected.
        options.JsonSerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
        options.JsonSerializerOptions.RespectNullableAnnotations = true;
        options.JsonSerializerOptions.RespectRequiredConstructorParameters = true;
      })
      .ConfigureApiBehaviorOptions(options =>
      {
        // Binding errors (bad JSON, unknown property, invalid route Guid) use the same error body as every other failure.
        options.InvalidModelStateResponseFactory = context =>
        {
          var errors = context.ModelState
            .Where(entry => entry.Value?.Errors.Count > 0)
            .SelectMany(entry => entry.Value!.Errors.Select(modelError =>
            {
              // "$.agentCard.capabilities[0].name" becomes "/agentCard/capabilities/0/name"
              var path = entry.Key.TrimStart('$', '.').Replace("[", ".").Replace("]", "").Replace('.', '/');

              var message = !string.IsNullOrEmpty(modelError.ErrorMessage)
                ? modelError.ErrorMessage
                : modelError.Exception?.Message ?? "The value is invalid.";

              // The serializer message for an unmapped member names an internal .NET type, so the one case the
              // spec cares about is replaced with an explicit message (spec section 1.2.1).
              if (message.Contains("'agentId'"))
                message = "The 'agentId' property is not allowed in a registration payload: agentId is issued by AERS.";

              return new ErrorDetail(path.Length == 0 ? "" : "/" + path, message);
            }))
            .ToList();

          return Result
            .Failure(ResultStatus.ValidationError, ErrorCodes.ValidationError, "The request payload is invalid.", errors)
            .ToErrorResult();
        };
      });

    services.AddOpenApi();
    services.AddExceptionHandler<GlobalExceptionHandler>();
    services.AddProblemDetails();

    // The API layer contributes the classifier for the exceptions only it knows about.
    services.AddSingleton<IExceptionClassifier, BadHttpRequestExceptionClassifier>();

    return services;
  }

  // Two credential types: registration key (register only) and agent API key (everything else), spec section 8.
  public static IServiceCollection AddApiAuthentication(this IServiceCollection services)
  {
    services
      .AddAuthentication()
      .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(AuthSchemes.ApiKey, null)
      .AddScheme<AuthenticationSchemeOptions, RegistrationKeyAuthenticationHandler>(AuthSchemes.RegistrationKey, null);
    services.AddAuthorization();

    return services;
  }
}

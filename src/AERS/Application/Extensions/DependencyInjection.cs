using System.Reflection;
using Application.Exceptions;
using Application.Interfaces.Errors;
using Application.Interfaces.Features;
using Application.Interfaces.Policies;
using Application.Policies;
using Application.Options;
using Application.Services;
using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Application.Extensions;

public static class DependencyInjection
{
  public static IServiceCollection AddApplicationServices(this IServiceCollection services)
  {
    services.TryAddSingleton(TimeProvider.System);
    services.AddScoped<IAgentRegistryService, AgentRegistryService>();

    // TEMPORARY (task A-05 replaces it with the DomainAccessPolicy from task B-01).
    services.AddSingleton<IDomainAccessPolicy, TemporaryDomainAccessPolicy>();

    // Each layer registers its own IExceptionClassifier; ErrorClassifier runs them all.
    services.AddSingleton<IExceptionClassifier, ApplicationExceptionClassifier>();
    services.AddSingleton<IErrorClassifier, ErrorClassifier>();

    return services;
  }

  public static IServiceCollection AddApplicationValidators(this IServiceCollection services)
  {
    services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly());

    return services;
  }

  public static IServiceCollection AddApplicationOptions(this IServiceCollection services, IConfiguration configuration)
  {
    services.AddOptions<AuthOptions>().Bind(configuration.GetSection(AuthOptions.SectionName));
    services.AddOptions<RegistrationLimitsOptions>().Bind(configuration.GetSection(RegistrationLimitsOptions.SectionName));
    services.AddOptions<AersPolicyOptions>().Bind(configuration.GetSection(AersPolicyOptions.SectionName));
    services
      .AddOptions<DiscoveryOptions>()
      .Bind(configuration.GetSection(DiscoveryOptions.SectionName))
      .Validate(options => options.MaxPageSize >= 1, "Aers:Discovery:MaxPageSize must be at least 1.")
      .Validate(options => options.DefaultPageSize >= 1 && options.DefaultPageSize <= options.MaxPageSize,
        "Aers:Discovery:DefaultPageSize must be between 1 and MaxPageSize.")
      .ValidateOnStart();

    return services;
  }
}

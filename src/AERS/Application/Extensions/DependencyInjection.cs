using System.Reflection;
using Application.Exceptions;
using Application.Interfaces.Errors;
using Application.Interfaces.Features;
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

    return services;
  }
}

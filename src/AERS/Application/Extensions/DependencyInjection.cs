using System.Reflection;
using Application.DTOs.Agent.Requests;
using Application.Interfaces.Features;
using Application.Services;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Application.Extensions;

public static class DependencyInjection
{
  public static IServiceCollection AddApplicationServices(this IServiceCollection services)
  {
    services.AddScoped<IAgentRegistryService, AgentRegistryService>();

    return services;
  }

  public static IServiceCollection AddApplicationValidators(this IServiceCollection services)
  {
    services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly());

    return services;
  }
}
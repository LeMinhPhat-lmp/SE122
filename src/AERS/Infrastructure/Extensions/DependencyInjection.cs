using Application.Interfaces.Repositories;
using Infrastructure.Options;
using Infrastructure.Persistences;
using Infrastructure.Persistences.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Infrastructure.Extensions;

public static class DependencyInjection
{
  public static IServiceCollection AddInfrastructureServices(this IServiceCollection services)
  {
    services.AddDbContext<AersDbContext>((serviceProvider, options) =>
    {
      var dbOptions = serviceProvider.GetRequiredService<IOptions<DbOptions>>().Value;

      options.UseNpgsql(dbOptions.ConnectionString);
    });
    services.AddScoped<IAgentRepository, AgentRepository>();
    services.AddScoped<IAgentCapabilityRepository, AgentCapabilityRepository>();
    services.AddScoped<IAgentCredentialRepository, AgentCredentialRepository>();
    services.AddScoped<IUnitOfWork, UnitOfWork>();

    return services;
  }

  public static IServiceCollection AddInfrastructureOptions(this IServiceCollection services, IConfiguration configuration)
  {
    services
      .AddOptions<DbOptions>()
      .Bind(configuration.GetSection(DbOptions.SectionName))
      .Validate(options => !string.IsNullOrWhiteSpace(options.ConnectionString), "Database connection string is required")
      .Validate(options => options.CommandTimeoutSeconds > 0, "Command timeout must be greater than zero")
      .ValidateOnStart();

    return services;
  }
}
using Application.Interfaces.Repositories;
using Application.Interfaces.Runtime;
using Application.Interfaces.Security;
using Application.Interfaces.Validation;
using Infrastructure.Options;
using Infrastructure.Persistences;
using Infrastructure.Persistences.Repositories;
using Infrastructure.Runtime;
using Infrastructure.Security;
using Infrastructure.Validation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Infrastructure.Extensions;

public static class DependencyInjection
{
  public static IServiceCollection AddInfrastructureServices(this IServiceCollection services)
  {
    services.AddDbContext<AersDbContext>((serviceProvider, options) =>
    {
      var dbOptions = serviceProvider.GetRequiredService<IOptions<DbOptions>>().Value;

      options.UseNpgsql(dbOptions.ConnectionString, npgsql => npgsql.CommandTimeout(dbOptions.CommandTimeoutSeconds));
      options.EnableSensitiveDataLogging(dbOptions.EnableSensitiveDataLogging);
      options.EnableDetailedErrors(dbOptions.EnableDetailedErrors);
    });
    services.AddScoped<IAgentRepository, AgentRepository>();
    services.AddScoped<IAgentCapabilityRepository, AgentCapabilityRepository>();
    services.AddScoped<IAgentCredentialRepository, AgentCredentialRepository>();
    services.AddScoped<IAuditEventRepository, AuditEventRepository>();
    services.AddScoped<IUnitOfWork, UnitOfWork>();

    
    // AbortOnConnectFail = false lets the app start while Redis is down; GET /health then reports 503.
    services.AddSingleton<IConnectionMultiplexer>(serviceProvider =>
    {
      var redisOptions = serviceProvider.GetRequiredService<IOptions<RedisOptions>>().Value;
      var configuration = ConfigurationOptions.Parse(redisOptions.ConnectionString);
      configuration.AbortOnConnectFail = false;
      return ConnectionMultiplexer.Connect(configuration);
    });
    services.AddScoped<IAgentPresenceStore, RedisAgentPresenceStore>();
    services.AddScoped<IDependencyHealthChecker, DependencyHealthChecker>();

    services.AddSingleton<IApiKeyService, ApiKeyService>();
    services.AddSingleton<IRegistrationKeyValidator, RegistrationKeyValidator>();
    services.AddSingleton<ISchemaMetaValidator, JsonSchemaMetaValidator>();

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

    services
      .AddOptions<RedisOptions>()
      .Bind(configuration.GetSection(RedisOptions.SectionName))
      .Validate(options => !string.IsNullOrWhiteSpace(options.ConnectionString), "Redis connection string is required")
      .ValidateOnStart();

    return services;
  }
}

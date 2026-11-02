using API.Extensions;
using API.Realtime;
using Application.Extensions;
using Application.Interfaces.Runtime;
using Application.Options;
using Infrastructure.Extensions;
using Infrastructure.Persistences;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// Decision A10: there is no default "open door". Outside Development the app refuses to start without a registration key.
var registrationKeys = builder.Configuration.GetSection(AuthOptions.SectionName + ":RegistrationKeys").Get<string[]>() ?? [];
if (!builder.Environment.IsDevelopment() && !registrationKeys.Any(key => !string.IsNullOrWhiteSpace(key)))
  throw new InvalidOperationException("Aers:Auth:RegistrationKeys must contain at least one key outside the Development environment.");

// Enforce the request body limit from configuration (Protocol Spec section 1.2.1).
var registrationLimits = builder.Configuration.GetSection(RegistrationLimitsOptions.SectionName).Get<RegistrationLimitsOptions>() ?? new RegistrationLimitsOptions();
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = registrationLimits.MaxBodyBytes);

// From API layer
builder.Services.AddApiServices();
builder.Services.AddApiAuthentication();
builder.Services.AddApiRealtime();

// From Infrastructure layer
builder.Services.AddInfrastructureServices();
builder.Services.AddInfrastructureOptions(builder.Configuration);

// From Application layer
builder.Services.AddApplicationOptions(builder.Configuration);
builder.Services.AddApplicationServices();
builder.Services.AddApplicationValidators();

var app = builder.Build();

// Decision A9: migrations are applied automatically in Development only; other environments apply them with a separate command.
if (app.Environment.IsDevelopment())
{
  using var scope = app.Services.CreateScope();
  await scope.ServiceProvider.GetRequiredService<AersDbContext>().Database.MigrateAsync();
}

// Decision A31: SignalR silently drops messages for unknown connection ids, so a connection mapping left over from a
// previous run of this instance would hide TARGET_UNAVAILABLE. Purge them before accepting connections.
using (var purgeScope = app.Services.CreateScope())
{
  var instanceId = purgeScope.ServiceProvider.GetRequiredService<IOptions<InstanceOptions>>().Value.Id;
  try
  {
    await purgeScope.ServiceProvider.GetRequiredService<IConnectionRegistry>().PurgeInstanceAsync(instanceId);
  }
  catch (Exception ex)
  {
    // Consistent with AbortOnConnectFail = false: the app starts even if Redis is down.
    app.Logger.LogWarning(ex, "Could not purge the stale connection mappings of instance {InstanceId} at startup.", instanceId);
  }
}

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
  app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<AgentHub>("/hubs/agent");

app.Run();

public partial class Program;

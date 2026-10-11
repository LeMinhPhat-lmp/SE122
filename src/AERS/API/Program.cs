using Application.Extensions;
using Infrastructure.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// From Infrastructure layer
builder.Services.AddInfrastructureServices();
builder.Services.AddInfrastructureOptions(builder.Configuration);

// From Application layer
builder.Services.AddApplicationServices();
builder.Services.AddApplicationValidators();


var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
  app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapControllers();

app.Run();
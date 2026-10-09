using System.Text.Json.Serialization;
using MeterDataService.Application;
using MeterDataService.Infrastructure;
using MeterDataService.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("MeterData")
    ?? throw new InvalidOperationException("Connection string 'MeterData' is not configured.");

builder.Services.AddApplication();
builder.Services.AddInfrastructure(connectionString);
builder.Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();

var app = builder.Build();

// Off by default: master data normally arrives from the market (UTILMD), not from the service itself.
if (app.Configuration.GetValue<bool>("SeedSampleMasterData"))
{
    await SampleMasterData.EnsureAsync(app.Services);
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

await app.RunAsync();

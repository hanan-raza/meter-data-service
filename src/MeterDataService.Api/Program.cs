using System.Text.Json.Serialization;
using MeterDataService.Application;
using MeterDataService.Infrastructure;
using MeterDataService.Infrastructure.Persistence;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("MeterData")
    ?? throw new InvalidOperationException("Connection string 'MeterData' is not configured.");

builder.Services.AddApplication();
builder.Services.AddInfrastructure(connectionString);
builder.Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi(options => options.AddDocumentTransformer((document, _, _) =>
{
    document.Info.Title = "meter-data-service";
    document.Info.Description =
        "Meter data management for the German energy market: CSV import of 15-minute values, validation, " +
        "gap filling (Ersatzwertbildung) and aggregation per market location (Marktlokation). " +
        "All days are German calendar days (Europe/Berlin), so a day has 92, 96 or 100 intervals.";
    return Task.CompletedTask;
}));

var app = builder.Build();

// Off by default: master data normally arrives from the market (UTILMD), not from the service itself.
if (app.Configuration.GetValue<bool>("SeedSampleMasterData"))
{
    await SampleMasterData.EnsureAsync(app.Services);
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

await app.RunAsync();

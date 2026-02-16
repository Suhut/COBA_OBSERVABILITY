using System.Diagnostics;
using System.Text;
using OpenTelemetry.Logs;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

// Define service name for OpenTelemetry
const string serviceName = "COBA_OBSERVABILITY";

// OpenObserve OTLP configuration
var openObserveConfig = builder.Configuration.GetSection("OpenObserve");
var otlpEndpoint = openObserveConfig["Endpoint"] ?? "http://localhost:5080/api/default";
var otlpUser = openObserveConfig["User"] ?? "root@example.com";
var otlpPassword = openObserveConfig["Password"] ?? "Complexpass#123";

var basicAuth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{otlpUser}:{otlpPassword}"));
var otlpAuthHeader = $"Authorization=Basic {basicAuth}";

// Configure OpenTelemetry Resource
var resourceBuilder = ResourceBuilder.CreateDefault()
    .AddService(serviceName)
    .AddAttributes(new Dictionary<string, object>
    {
        ["environment"] = builder.Environment.EnvironmentName,
        ["service.version"] = "1.0.0"
    });

// Configure OpenTelemetry Logging
builder.Logging.ClearProviders();
builder.Logging.AddOpenTelemetry(logging =>
{
    logging.IncludeFormattedMessage = true;
    logging.SetResourceBuilder(resourceBuilder)
        .AddConsoleExporter()
        .AddOtlpExporter(opt =>
        {
            opt.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.HttpProtobuf;
            //opt.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.Grpc;
            opt.Endpoint = new Uri($"{otlpEndpoint}/v1/logs");
            //opt.Endpoint = new Uri($"{otlpEndpoint}");
            opt.Headers = otlpAuthHeader;
        });
});

// Configure OpenTelemetry Tracing
builder.Services.AddOpenTelemetry()
     //.WithLogging(logging =>
     //{
     //    logging.AddConsoleExporter()
     //           .AddOtlpExporter(opt =>
     //           {
     //               opt.Endpoint = new Uri($"{otlpEndpoint}/v1/logs");
     //               opt.Headers = otlpAuthHeader;
     //               opt.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.HttpProtobuf;
     //           });
     //})
    .WithTracing(tracing => tracing
         .SetResourceBuilder(resourceBuilder)
        //.ConfigureResource(resourceBuilder =>
        //{
        //    resourceBuilder.AddService(
        //        builder.Environment.ApplicationName,
        //        builder.Environment.EnvironmentName,
        //        "1.0",
        //        false,
        //        Environment.MachineName);
        //})
        .AddSource(serviceName)
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddConsoleExporter()
        .AddOtlpExporter(opt =>
        {
            //opt.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.Grpc;
            opt.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.HttpProtobuf;
            opt.Endpoint = new Uri($"{otlpEndpoint}");
            //opt.Endpoint = new Uri($"{otlpEndpoint}");
            opt.Headers = otlpAuthHeader;
        }));

// Add services to the container.
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Create ActivitySource for custom tracing
var activitySource = new ActivitySource(serviceName);

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.MapGet("/force", () =>
{
    using var activity = new Activity("manual-test");
    activity.Start();

    Console.WriteLine($"TRACE ID: {activity.TraceId}");

    activity.Stop();
    return "ok";
});

app.MapGet("/weatherforecast", (ILogger<Program> logger) =>
{
    using var activity = activitySource.StartActivity("GenerateWeatherForecast");
    activity?.SetTag("forecast.requested", true);

    logger.LogInformation("Generating weather forecast data");

    var forecast = Enumerable.Range(1, 5).Select(index =>
                    new WeatherForecast
                    (
                        DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
                        Random.Shared.Next(-20, 55),
                        summaries[Random.Shared.Next(summaries.Length)]
                    ))
                    .ToArray();

    activity?.SetTag("forecast.count", forecast.Length);
    logger.LogInformation("Generated {Count} weather forecasts", forecast.Length);

    return forecast;
})
.WithName("GetWeatherForecast");

app.Run();

internal record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}

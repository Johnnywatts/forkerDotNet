using Forker.Service;
using Serilog;

// Configure Serilog (placeholder configuration)
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(formatProvider: System.Globalization.CultureInfo.InvariantCulture)
    .WriteTo.File("logs/forker-.txt", rollingInterval: RollingInterval.Day, formatProvider: System.Globalization.CultureInfo.InvariantCulture)
    .CreateLogger();

try
{
    var builder = Host.CreateApplicationBuilder(args);

    // Add Serilog
    builder.Services.AddSerilog();

    // Add health checks
    builder.Services.AddHealthChecks();

    // Add hosted services
    builder.Services.AddHostedService<Worker>();
    builder.Services.AddHostedService<HealthService>();

    var host = builder.Build();

    Log.Information("Forker Service starting...");
    await host.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

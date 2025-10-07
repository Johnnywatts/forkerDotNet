using Forker.Infrastructure.Configuration;
using Forker.Infrastructure.Database;
using Forker.Infrastructure.DependencyInjection;
using Forker.Service;
using Serilog;

// Configure Serilog from configuration
var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";
var configuration = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile($"appsettings.{environment}.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables()
    .Build();

Log.Information("Starting with environment: {Environment}", environment);

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(configuration)
    .CreateLogger();

try
{
    Log.Information("Forker Service starting...");

    var builder = Host.CreateApplicationBuilder(args);

    // Enable Windows Service support
    var serviceName = builder.Configuration.GetValue<string>("ServiceName") ?? "ForkerDotNet";
    builder.Services.AddWindowsService(options =>
    {
        options.ServiceName = serviceName;
    });

    Log.Information("Service configured with name: {ServiceName}", serviceName);

    // Add Serilog
    builder.Services.AddSerilog();

    // Configure options from appsettings.json
    builder.Services.Configure<DatabaseConfiguration>(builder.Configuration.GetSection("Database"));
    builder.Services.Configure<DirectoryConfiguration>(builder.Configuration.GetSection("Directories"));
    builder.Services.Configure<FileMonitoringConfiguration>(builder.Configuration.GetSection("Monitoring"));
    builder.Services.Configure<TargetConfiguration>(builder.Configuration.GetSection("Target"));
    builder.Services.Configure<TestingConfiguration>(builder.Configuration.GetSection("Testing"));

    // Register ForkerDotNet infrastructure services
    builder.Services.AddForkerInfrastructure();

    // Add health checks
    builder.Services.AddHealthChecks();

    // Add hosted services
    builder.Services.AddHostedService<Worker>();
    builder.Services.AddHostedService<HealthService>();

    var host = builder.Build();

    // Initialize database on startup
    Log.Information("Initializing database...");
    await host.Services.InitializeForkerDatabaseAsync();
    Log.Information("Database initialized successfully");

    Log.Information("Forker Service started - Ready to process files");
    await host.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
    throw;
}
finally
{
    Log.Information("Forker Service stopping...");
    Log.CloseAndFlush();
}

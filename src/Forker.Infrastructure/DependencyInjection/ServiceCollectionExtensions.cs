using Forker.Domain.Repositories;
using Forker.Domain.Services;
using Forker.Infrastructure.Configuration;
using Forker.Infrastructure.Database;
using Forker.Infrastructure.Repositories;
using Forker.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Forker.Infrastructure.DependencyInjection;

/// <summary>
/// Extension methods for configuring infrastructure services in dependency injection container.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds ForkerDotNet infrastructure services to the service collection.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configureDatabase">Optional database configuration action</param>
    /// <param name="configureTargets">Optional target configuration action</param>
    /// <returns>The service collection for method chaining</returns>
    public static IServiceCollection AddForkerInfrastructure(
        this IServiceCollection services,
        Action<DatabaseConfiguration>? configureDatabase = null,
        Action<TargetConfiguration>? configureTargets = null)
    {
        // Configure database options
        if (configureDatabase != null)
        {
            services.Configure(configureDatabase);
        }
        else
        {
            services.Configure<DatabaseConfiguration>(_ => { }); // Use defaults
        }

        // Configure target options
        if (configureTargets != null)
        {
            services.Configure(configureTargets);
        }
        else
        {
            services.Configure<TargetConfiguration>(_ => { }); // Use defaults
        }

        // Register database services
        services.AddSingleton<ISqliteConnectionFactory, SqliteConnectionFactory>();

        // Register repositories
        services.AddScoped<IJobRepository, SqliteJobRepository>();
        services.AddScoped<ITargetOutcomeRepository, SqliteTargetOutcomeRepository>();

        // Register file discovery services
        services.AddSingleton<IFileStabilityChecker, FileStabilityChecker>();
        services.AddSingleton<IFileDiscoveryService, FileDiscoveryService>();

        // Register file copy services
        services.AddScoped<IHashingService, HashingService>();
        services.AddScoped<IFileCopyService, FileCopyService>();
        services.AddScoped<ICopyOrchestrator, CopyOrchestrator>();

        return services;
    }

    /// <summary>
    /// Initializes the ForkerDotNet database during application startup.
    /// Should be called during application configuration.
    /// </summary>
    /// <param name="serviceProvider">The service provider</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public static async Task InitializeForkerDatabaseAsync(
        this IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default)
    {
        using var scope = serviceProvider.CreateScope();
        var connectionFactory = scope.ServiceProvider.GetRequiredService<ISqliteConnectionFactory>();
        await connectionFactory.InitializeDatabaseAsync(cancellationToken);
    }
}
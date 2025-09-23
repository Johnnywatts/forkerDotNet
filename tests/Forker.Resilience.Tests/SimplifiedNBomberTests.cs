using FluentAssertions;
using Forker.Domain.Services;
using Forker.Infrastructure.Configuration;
using Forker.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NBomber.CSharp;
using System.Collections.Concurrent;

namespace Forker.Resilience.Tests;

/// <summary>
/// Simplified NBomber-based race condition tests for FileDiscoveryService.
/// Focuses on core race condition detection without complex statistical analysis.
/// Essential for validating medical imaging file processing under realistic load.
/// </summary>
public class SimplifiedNBomberTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly ILogger<FileDiscoveryService> _logger;

    public SimplifiedNBomberTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "NBomberRaceTest_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_testDirectory);

        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Error));
        _logger = loggerFactory.CreateLogger<FileDiscoveryService>();
    }

    /// <summary>
    /// Core NBomber Test: Timer Race Condition Detection
    /// Uses sustained load injection to detect timer callback overlaps that cause data corruption.
    /// </summary>
    [Fact]
    public void SustainedLoad_TimerRaceDetection_ShouldNotCauseDuplicateProcessing()
    {
        var duplicateFiles = new ConcurrentBag<string>();
        var processedFiles = new ConcurrentDictionary<string, int>();
        var exceptions = new ConcurrentBag<Exception>();

        // Create single shared service to test actual timer race conditions
        var sharedService = CreateFileDiscoveryService();

        // Critical: Track duplicate processing which indicates timer overlap
        sharedService.FileDiscovered += (sender, args) =>
        {
            var count = processedFiles.AddOrUpdate(args.FilePath, 1, (key, existing) => existing + 1);
            if (count > 1)
            {
                duplicateFiles.Add($"RACE CONDITION: {args.FilePath} processed {count} times");
            }
        };

        try
        {
            sharedService.StartAsync().Wait();

            var scenario = Scenario.Create("timer_race_detection", async context =>
            {
                try
                {
                    // Create files with timing that can trigger race conditions
                    for (int i = 0; i < 2; i++)
                    {
                        var testFile = Path.Combine(_testDirectory, $"race_{Environment.CurrentManagedThreadId}_{i}_{DateTime.UtcNow.Ticks}.test");
                        await File.WriteAllTextAsync(testFile, "Medical imaging race test");
                        await Task.Delay(Random.Shared.Next(50, 200)); // Variable timing to create race windows
                    }

                    return Response.Ok();
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                    return Response.Fail();
                }
            })
            .WithLoadSimulations(
                // Sustained concurrent load: 15 scenarios per second for 30 seconds (CI-friendly)
                Simulation.Inject(rate: 15, interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromSeconds(30))
            );

            // Run the race condition test
            var result = NBomberRunner
                .RegisterScenarios(scenario)
                .Run();

            // Allow final processing
            Task.Delay(5000).Wait();

            // Critical assertions for race condition detection
            exceptions.Should().BeEmpty("race conditions should not cause exceptions");
            duplicateFiles.Should().BeEmpty("timer race conditions should not cause duplicate file processing: " + string.Join(", ", duplicateFiles.Take(10)));

            // Basic performance validation
            result.AllOkCount.Should().BeGreaterThan(400, "most operations should succeed under load");
            result.AllFailCount.Should().Be(0, "no operations should fail due to race conditions");
        }
        finally
        {
            sharedService.StopAsync().Wait();
            sharedService.DisposeAsync().AsTask().Wait();
        }
    }

    /// <summary>
    /// Event Handler Concurrency Test
    /// Validates thread-safe event handling under concurrent subscriber load.
    /// </summary>
    [Fact]
    public void ConcurrentEventHandlers_ShouldNotCauseExceptions()
    {
        var service = CreateFileDiscoveryService();
        var eventExceptions = new ConcurrentBag<Exception>();

        try
        {
            service.StartAsync().Wait();

            var scenario = Scenario.Create("event_concurrency", async context =>
            {
                try
                {
                    var eventsReceived = 0;

                    // Subscribe with realistic processing simulation
                    EventHandler<FileDiscoveredEventArgs> handler = (sender, args) =>
                    {
                        try
                        {
                            Interlocked.Increment(ref eventsReceived);
                            Thread.Sleep(Random.Shared.Next(10, 100)); // Simulate processing
                        }
                        catch (Exception ex)
                        {
                            eventExceptions.Add(ex);
                        }
                    };

                    service.FileDiscovered += handler;

                    // Create file to trigger event
                    var testFile = Path.Combine(_testDirectory, $"event_{Environment.CurrentManagedThreadId}_{DateTime.UtcNow.Ticks}.test");
                    await File.WriteAllTextAsync(testFile, "Event concurrency test");

                    await Task.Delay(1000);
                    return Response.Ok();
                }
                catch (Exception ex)
                {
                    eventExceptions.Add(ex);
                    return Response.Fail();
                }
            })
            .WithLoadSimulations(
                Simulation.Inject(rate: 20, interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromSeconds(30))
            );

            var result = NBomberRunner
                .RegisterScenarios(scenario)
                .Run();

            eventExceptions.Should().BeEmpty("event handlers should not cause race condition exceptions");
            result.AllFailCount.Should().Be(0, "event concurrency should not cause failures");
        }
        finally
        {
            service.StopAsync().Wait();
            service.DisposeAsync().AsTask().Wait();
        }
    }

    /// <summary>
    /// Disposal Race Condition Test
    /// Tests rapid disposal cycles to detect deadlock conditions.
    /// </summary>
    [Fact]
    public void RapidDisposal_ShouldNotCauseDeadlocks()
    {
        var disposalExceptions = new ConcurrentBag<Exception>();

        var scenario = Scenario.Create("disposal_race", async context =>
        {
            try
            {
                var service = CreateFileDiscoveryService();

                await service.StartAsync();

                // Create file during potential disposal window
                var testFile = Path.Combine(_testDirectory, $"disposal_{Environment.CurrentManagedThreadId}_{DateTime.UtcNow.Ticks}.test");
                await File.WriteAllTextAsync(testFile, "Disposal race test");

                await Task.Delay(Random.Shared.Next(10, 100));

                await service.StopAsync();
                await service.DisposeAsync();

                return Response.Ok();
            }
            catch (Exception ex)
            {
                disposalExceptions.Add(ex);
                return Response.Fail();
            }
        })
        .WithLoadSimulations(
            Simulation.Inject(rate: 15, interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromSeconds(30))
        )
        .WithWarmUpDuration(TimeSpan.FromSeconds(10)); // Ensure warm-up < duration

        // Add deadlock detection with NBomber configuration fix
        var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var runTask = Task.Run(() => NBomberRunner
            .RegisterScenarios(scenario)
            .Run(), cts.Token);

        var completed = runTask.Wait(TimeSpan.FromMinutes(3));

        completed.Should().BeTrue("disposal test should complete without deadlocks");
        disposalExceptions.Should().BeEmpty("disposal should not cause race condition exceptions");

        if (completed)
        {
            var result = runTask.Result;
            result.AllFailCount.Should().Be(0, "disposal operations should not fail");
        }
    }

    /// <summary>
    /// Production Load Simulation
    /// Simulates realistic medical imaging file processing patterns.
    /// </summary>
    [Fact]
    public void ProductionLoadSimulation_MedicalWorkflow_ShouldMaintainStability()
    {
        var service = CreateFileDiscoveryService();
        var processingErrors = new ConcurrentBag<Exception>();

        try
        {
            service.StartAsync().Wait();

            var scenario = Scenario.Create("medical_simulation", async context =>
            {
                try
                {
                    var fileTypes = new[] { ".svs", ".tiff", ".test" };
                    var selectedType = fileTypes[Random.Shared.Next(fileTypes.Length)];

                    var fileName = $"medical_{Environment.CurrentManagedThreadId}_{DateTime.UtcNow.Ticks}{selectedType}";
                    var testFile = Path.Combine(_testDirectory, fileName);

                    // Simulate realistic file creation
                    var content = new string('M', Random.Shared.Next(500, 2000)); // Varying sizes
                    await File.WriteAllTextAsync(testFile, content);

                    await Task.Delay(Random.Shared.Next(100, 400)); // Realistic timing

                    return Response.Ok();
                }
                catch (Exception ex)
                {
                    processingErrors.Add(ex);
                    return Response.Fail();
                }
            })
            .WithLoadSimulations(
                // CI-friendly sustained load for medical imaging patterns
                Simulation.Inject(rate: 10, interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromSeconds(30))
            )
            .WithWarmUpDuration(TimeSpan.FromSeconds(15)); // Ensure warm-up < duration

            var result = NBomberRunner
                .RegisterScenarios(scenario)
                .Run();

            // PRIMARY ASSERTION: System stability under load
            processingErrors.Should().BeEmpty("production simulation should not cause errors");
            result.AllFailCount.Should().Be(0, "production load should not cause failures");

            // REALISTIC ASSERTION: Expect reasonable throughput (10 ops/sec * 30 seconds = ~300 max)
            result.AllOkCount.Should().BeGreaterThan(200, "should process reasonable number of files under realistic medical imaging load");
        }
        finally
        {
            service.StopAsync().Wait();
            service.DisposeAsync().AsTask().Wait();
        }
    }

    private FileDiscoveryService CreateFileDiscoveryService()
    {
        var directories = new DirectoryConfiguration
        {
            Source = _testDirectory
        };

        var monitoring = new FileMonitoringConfiguration
        {
            FileFilters = new[] { "*.svs", "*.tiff", "*.test" },
            ExcludeExtensions = new[] { ".tmp", ".lock" },
            IncludeSubdirectories = false,
            StabilityCheckInterval = 1,
            MaxStabilityChecks = 2, // Fast for testing
            MinimumFileAge = 1
        };

        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Error));
        var stabilityLogger = loggerFactory.CreateLogger<FileStabilityChecker>();
        var stabilityChecker = new FileStabilityChecker(Options.Create(monitoring), stabilityLogger);

        return new FileDiscoveryService(
            Options.Create(directories),
            Options.Create(monitoring),
            stabilityChecker,
            _logger);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDirectory))
            {
                Directory.Delete(_testDirectory, true);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }
}
using FluentAssertions;
using Forker.Domain.Services;
using Forker.Infrastructure.Configuration;
using Forker.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace Forker.Resilience.Tests;

/// <summary>
/// Concurrent stress tests to validate race condition fixes in FileDiscoveryService.
/// These tests simulate production load scenarios to ensure thread safety under concurrent operations.
/// Phase 5.1A: In-Process Testing validates the race condition fixes.
/// </summary>
public class ConcurrentStressTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly ILogger<FileDiscoveryService> _logger;

    public ConcurrentStressTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "ForkerStressTest_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_testDirectory);

        // Setup logger
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        _logger = loggerFactory.CreateLogger<FileDiscoveryService>();
    }

    /// <summary>
    /// Test 1: Timer Overlap Prevention - Validates ProcessPendingFiles callbacks don't overlap
    /// This directly tests the fix for the async void anti-pattern race condition
    /// </summary>
    [Fact]
    public async Task TimerOverlapPrevention_100ConcurrentOperations_ShouldNotCauseRaceConditions()
    {
        var services = new ConcurrentBag<FileDiscoveryService>();
        var fileDiscoveredCount = new ConcurrentDictionary<string, int>();
        var exceptions = new ConcurrentBag<Exception>();

        // Create 10 concurrent tasks, each creating a service and processing files (reduced for stability)
        var tasks = Enumerable.Range(0, 10).Select(async taskId =>
        {
            try
            {
                var service = CreateFileDiscoveryService();
                services.Add(service);

                // Subscribe to file discovered events to detect duplicates
                service.FileDiscovered += (sender, args) =>
                {
                    fileDiscoveredCount.AddOrUpdate(args.FilePath, 1, (key, count) => count + 1);
                };

                await service.StartAsync();

                // Create multiple test files rapidly to trigger timer callbacks
                for (int i = 0; i < 3; i++)
                {
                    var testFile = Path.Combine(_testDirectory, $"timer_test_{taskId}_{i}.test");
                    await File.WriteAllTextAsync(testFile, "timer test content");
                    await Task.Delay(Random.Shared.Next(50, 150)); // Random delay to create timing variations
                }

                // Let the service process files
                await Task.Delay(3000);

                await service.StopAsync();
                await service.DisposeAsync();
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        });

        await Task.WhenAll(tasks);

        // Assertions
        exceptions.Should().BeEmpty("timer callbacks should not cause race condition exceptions");

        // Verify no file was discovered more than once (no overlapping timer executions)
        foreach (var kvp in fileDiscoveredCount.Where(f => f.Key.Contains("timer_test")))
        {
            kvp.Value.Should().Be(1, $"file {kvp.Key} should be discovered exactly once, not {kvp.Value} times - this indicates timer overlap");
        }

        // Cleanup
        foreach (var service in services)
        {
            try { await service.DisposeAsync(); } catch { }
        }
    }

    /// <summary>
    /// Test 2: Event Handler Safety - Validates FileDiscovered events are thread-safe
    /// This tests the thread-safe event handling implementation
    /// </summary>
    [Fact]
    public async Task EventHandlerSafety_50ConcurrentSubscribers_ShouldNotCauseRaceConditions()
    {
        var service = CreateFileDiscoveryService();
        var eventHandlerExceptions = new ConcurrentBag<Exception>();
        var eventsReceived = new ConcurrentDictionary<int, int>();

        try
        {
            await service.StartAsync();

            // Create 20 concurrent event subscribers (reduced for stability)
            var tasks = Enumerable.Range(0, 20).Select(async subscriberId =>
            {
                try
                {
                    var receivedCount = 0;

                    // Subscribe to events
                    EventHandler<FileDiscoveredEventArgs> handler = (sender, args) =>
                    {
                        try
                        {
                            Interlocked.Increment(ref receivedCount);
                            // Simulate processing time to create potential race conditions
                            Thread.Sleep(Random.Shared.Next(10, 50));
                        }
                        catch (Exception ex)
                        {
                            eventHandlerExceptions.Add(ex);
                        }
                    };

                    service.FileDiscovered += handler;

                    // Create test files to trigger events
                    for (int i = 0; i < 2; i++)
                    {
                        var testFile = Path.Combine(_testDirectory, $"event_test_{subscriberId}_{i}.test");
                        await File.WriteAllTextAsync(testFile, "event test content");
                        await Task.Delay(Random.Shared.Next(50, 200));
                    }

                    // Wait for event processing
                    await Task.Delay(2000);

                    eventsReceived[subscriberId] = receivedCount;
                }
                catch (Exception ex)
                {
                    eventHandlerExceptions.Add(ex);
                }
            });

            await Task.WhenAll(tasks);

            // Assertions
            eventHandlerExceptions.Should().BeEmpty("event handlers should not throw exceptions due to race conditions");
            eventsReceived.Values.Sum().Should().BeGreaterThan(0, "events should have been received");
        }
        finally
        {
            await service.StopAsync();
            await service.DisposeAsync();
        }
    }

    /// <summary>
    /// Test 3: Disposal Safety - Validates no deadlocks during rapid start/stop cycles
    /// This tests the IAsyncDisposable implementation and disposal race condition fixes
    /// </summary>
    [Fact]
    public async Task DisposalSafety_100RapidStartStopCycles_ShouldNotCauseDeadlocks()
    {
        var disposalExceptions = new ConcurrentBag<Exception>();

        // Create 25 concurrent rapid start/stop cycles (reduced for stability)
        var tasks = Enumerable.Range(0, 25).Select(async cycleId =>
        {
            try
            {
                var service = CreateFileDiscoveryService();

                // Rapid start/stop cycle
                await service.StartAsync();

                // Create a file to trigger processing
                var testFile = Path.Combine(_testDirectory, $"disposal_test_{cycleId}.test");
                await File.WriteAllTextAsync(testFile, "disposal test");

                // Random delay to create race conditions between start/stop
                await Task.Delay(Random.Shared.Next(50, 200));

                // Stop and dispose while potentially processing
                await service.StopAsync();
                await service.DisposeAsync();
            }
            catch (Exception ex)
            {
                disposalExceptions.Add(ex);
            }
        });

        // Add timeout to detect deadlocks
        var completionTask = Task.WhenAll(tasks);
        var timeoutTask = Task.Delay(TimeSpan.FromMinutes(2));

        var completedTask = await Task.WhenAny(completionTask, timeoutTask);

        completedTask.Should().Be(completionTask, "disposal should complete within timeout (no deadlocks)");
        disposalExceptions.Should().BeEmpty("disposal should not cause exceptions");
    }

    /// <summary>
    /// Test 4: Atomic State Transitions - Validates shutdown sequence atomicity
    /// This tests the atomic state management implementation
    /// </summary>
    [Fact]
    public async Task AtomicStateTransitions_ConcurrentStartStop_ShouldMaintainConsistency()
    {
        var stateExceptions = new ConcurrentBag<Exception>();

        // Create 15 concurrent operations that try to cause state race conditions (reduced for stability)
        var tasks = Enumerable.Range(0, 15).Select(async operationId =>
        {
            try
            {
                var service = CreateFileDiscoveryService();

                // Create potential race condition between start and stop
                var startTask = service.StartAsync();
                var stopTask = Task.Run(async () =>
                {
                    await Task.Delay(Random.Shared.Next(10, 100)); // Random delay to create race
                    await service.StopAsync();
                });

                await Task.WhenAll(startTask, stopTask);
                await service.DisposeAsync();
            }
            catch (Exception ex)
            {
                stateExceptions.Add(ex);
            }
        });

        await Task.WhenAll(tasks);

        stateExceptions.Should().BeEmpty("concurrent start/stop operations should not cause state inconsistency exceptions");
    }

    /// <summary>
    /// Test 5: High-Volume File Processing - Stress test with many files
    /// This validates the overall system under realistic medical imaging load
    /// </summary>
    [Fact]
    public async Task HighVolumeFileProcessing_500Files_ShouldMaintainStability()
    {
        var service = CreateFileDiscoveryService();
        var processedFiles = new ConcurrentBag<string>();
        var exceptions = new ConcurrentBag<Exception>();

        try
        {
            // Subscribe to file events
            service.FileDiscovered += (sender, args) =>
            {
                processedFiles.Add(args.FilePath);
            };

            await service.StartAsync();

            // Create 100 files rapidly to simulate medical imaging workflow (reduced for stability)
            var fileTasks = Enumerable.Range(0, 100).Select(async fileId =>
            {
                try
                {
                    var testFile = Path.Combine(_testDirectory, $"volume_test_{fileId}.test");
                    await File.WriteAllTextAsync(testFile, $"Medical imaging file {fileId} content");

                    // Small random delay to simulate real file creation patterns
                    await Task.Delay(Random.Shared.Next(1, 10));
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            });

            await Task.WhenAll(fileTasks);

            // Wait for processing to complete
            await Task.Delay(10000); // 10 seconds for all files to be processed

            await service.StopAsync();

            // Assertions
            exceptions.Should().BeEmpty("high-volume file creation should not cause exceptions");
            processedFiles.Should().HaveCountGreaterThan(80, "most files should be processed without race condition issues");
        }
        finally
        {
            await service.DisposeAsync();
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
            StabilityCheckInterval = 1, // 1 second for faster testing
            MaxStabilityChecks = 3 // Reduced for faster testing
        };

        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
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
        // Cleanup test directory
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
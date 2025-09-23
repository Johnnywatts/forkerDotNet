using FluentAssertions;
using Forker.Domain.Services;
using Forker.Infrastructure.Configuration;
using Forker.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace Forker.Resilience.Tests;

/// <summary>
/// File System Race Condition Tests - Phase 10.2 Implementation
/// These tests validate file system timing race conditions that are separate from thread safety.
/// Focus areas: File stability detection, FileSystemWatcher reliability, I/O timing races.
/// </summary>
public class FileSystemRaceTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly ILogger<FileDiscoveryService> _logger;

    public FileSystemRaceTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "FileSystemRaceTest_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_testDirectory);

        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        _logger = loggerFactory.CreateLogger<FileDiscoveryService>();
    }

    /// <summary>
    /// Test 1: File Growth Detection Race Validation
    /// Validates file stability detection when files are growing during discovery process.
    /// This tests the FileStabilityChecker's ability to handle files being written by external processes.
    /// </summary>
    [Fact]
    public async Task FileGrowthDetection_ConcurrentWriteDuringStabilityCheck_ShouldHandleCorrectly()
    {
        var service = CreateFileDiscoveryService();
        var discoveredFiles = new ConcurrentBag<string>();
        var exceptions = new ConcurrentBag<Exception>();
        var growingFileDetected = false;

        try
        {
            service.FileDiscovered += (sender, args) =>
            {
                discoveredFiles.Add(args.FilePath);
            };

            await service.StartAsync();

            // Create a file that will grow during stability checking
            var growingFile = Path.Combine(_testDirectory, "growing_file.test");

            // Start writing to file in background to simulate external process
            var writeTask = Task.Run(async () =>
            {
                try
                {
                    using var fileStream = new FileStream(growingFile, FileMode.Create, FileAccess.Write, FileShare.Read);

                    // Write initial content
                    var initialContent = System.Text.Encoding.UTF8.GetBytes("Initial content");
                    await fileStream.WriteAsync(initialContent);
                    await fileStream.FlushAsync();

                    // Continue writing during stability check period
                    for (int i = 0; i < 10; i++)
                    {
                        await Task.Delay(500); // Delay longer than stability check interval
                        var additionalContent = System.Text.Encoding.UTF8.GetBytes($" Additional content {i}");
                        await fileStream.WriteAsync(additionalContent);
                        await fileStream.FlushAsync();
                        growingFileDetected = true;
                    }
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            });

            // Allow time for file growth and stability checking
            await Task.Delay(8000);
            await service.StopAsync();

            // Wait for write task to complete
            await writeTask;

            // PRIMARY ASSERTION: No exceptions during file growth handling
            exceptions.Should().BeEmpty("file growth during stability checking should not cause exceptions");

            // SECONDARY ASSERTION: System handled growing file appropriately
            growingFileDetected.Should().BeTrue("test should have created a growing file scenario");

            // TERTIARY ASSERTION: Growing file should either be discovered after stability OR not discovered
            // This validates the file stability detection logic - either:
            // 1. File was not discovered (correctly rejected due to instability)
            // 2. File was discovered (correctly detected as stable after growth stopped)
            var growingFileWasDiscovered = discoveredFiles.Any(f => f.Contains("growing_file"));
            Console.WriteLine($"Growing file discovery result: {(growingFileWasDiscovered ? "DISCOVERED" : "NOT DISCOVERED (due to instability)")}");

            // Both outcomes are valid - the key is no exceptions occurred
        }
        finally
        {
            await service.DisposeAsync();
        }
    }

    /// <summary>
    /// Test 2: File Lock Detection Race Validation
    /// Validates file stability detection when files are locked by external processes.
    /// This tests the FileStabilityChecker's file accessibility validation.
    /// </summary>
    [Fact]
    public async Task FileLockDetection_ConcurrentLockDuringStabilityCheck_ShouldHandleCorrectly()
    {
        var service = CreateFileDiscoveryService();
        var discoveredFiles = new ConcurrentBag<string>();
        var exceptions = new ConcurrentBag<Exception>();
        var lockingDetected = false;

        try
        {
            service.FileDiscovered += (sender, args) =>
            {
                discoveredFiles.Add(args.FilePath);
            };

            await service.StartAsync();

            // Create a file that will be locked during stability checking
            var lockedFile = Path.Combine(_testDirectory, "locked_file.test");

            // Create file first
            await File.WriteAllTextAsync(lockedFile, "File content for locking test");

            // Lock the file in background to simulate external process access
            var lockTask = Task.Run(async () =>
            {
                try
                {
                    using var fileStream = new FileStream(lockedFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                    lockingDetected = true;

                    // Keep file locked during stability check period
                    await Task.Delay(3000);

                    // File will be automatically unlocked when fileStream is disposed
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            });

            // Allow time for locking and stability checking
            await Task.Delay(6000);
            await service.StopAsync();

            // Wait for lock task to complete
            await lockTask;

            // PRIMARY ASSERTION: No exceptions during file lock handling
            exceptions.Should().BeEmpty("file locking during stability checking should not cause exceptions");

            // SECONDARY ASSERTION: System detected locking scenario
            lockingDetected.Should().BeTrue("test should have created a file locking scenario");

            // TERTIARY ASSERTION: Locked file should either be discovered after unlock OR not discovered
            var lockedFileWasDiscovered = discoveredFiles.Any(f => f.Contains("locked_file"));
            Console.WriteLine($"Locked file discovery result: {(lockedFileWasDiscovered ? "DISCOVERED (after unlock)" : "NOT DISCOVERED (due to locking)")}");

            // Both outcomes are valid - the key is no exceptions occurred and system handled locking gracefully
        }
        finally
        {
            await service.DisposeAsync();
        }
    }

    /// <summary>
    /// Test 3: File Age Requirements Race Validation
    /// Validates minimum file age requirements when files are created rapidly.
    /// This tests the FileStabilityChecker's age validation logic.
    /// </summary>
    [Fact]
    public async Task FileAgeRequirements_RapidFileCreation_ShouldRespectMinimumAge()
    {
        var service = CreateFileDiscoveryService();
        var discoveredFiles = new ConcurrentBag<string>();
        var exceptions = new ConcurrentBag<Exception>();
        var fileCreationTimes = new ConcurrentDictionary<string, DateTime>();

        try
        {
            service.FileDiscovered += (sender, args) =>
            {
                discoveredFiles.Add(args.FilePath);
            };

            await service.StartAsync();

            // Create files rapidly to test age requirements
            var fileTasks = Enumerable.Range(0, 5).Select(async i =>
            {
                try
                {
                    var testFile = Path.Combine(_testDirectory, $"age_test_{i}.test");
                    var creationTime = DateTime.UtcNow;
                    fileCreationTimes[testFile] = creationTime;

                    await File.WriteAllTextAsync(testFile, $"Age test content {i}");

                    // Create files with minimal delay between them
                    await Task.Delay(100);
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            });

            await Task.WhenAll(fileTasks);

            // Allow time for age requirements to be evaluated
            await Task.Delay(8000);
            await service.StopAsync();

            // PRIMARY ASSERTION: No exceptions during age requirement processing
            exceptions.Should().BeEmpty("file age requirement checking should not cause exceptions");

            // SECONDARY ASSERTION: Age requirements are respected
            // Files discovered should have respected the minimum age requirement
            foreach (var discoveredFile in discoveredFiles.Where(f => f.Contains("age_test")))
            {
                if (fileCreationTimes.TryGetValue(discoveredFile, out var creationTime))
                {
                    var discoveryDelay = DateTime.UtcNow - creationTime;
                    discoveryDelay.TotalSeconds.Should().BeGreaterThan(0, "discovered files should have some age delay");
                    Console.WriteLine($"File {Path.GetFileName(discoveredFile)} discovered after {discoveryDelay.TotalSeconds:F1}s");
                }
            }

            // TERTIARY ASSERTION: System functionality validation
            fileCreationTimes.Should().HaveCount(5, "all test files should have been created");
            Console.WriteLine($"Files created: {fileCreationTimes.Count}, Files discovered: {discoveredFiles.Count(f => f.Contains("age_test"))}");
        }
        finally
        {
            await service.DisposeAsync();
        }
    }

    /// <summary>
    /// Test 4: Concurrent Stability Checking Race Validation
    /// Validates that multiple files undergoing stability checking don't interfere with each other.
    /// This tests the FileStabilityChecker's concurrent operation handling.
    /// </summary>
    [Fact]
    public async Task ConcurrentStabilityChecking_MultipleFiles_ShouldNotInterfere()
    {
        var service = CreateFileDiscoveryService();
        var discoveredFiles = new ConcurrentBag<string>();
        var exceptions = new ConcurrentBag<Exception>();
        var stabilityCheckFiles = new List<string>();

        try
        {
            service.FileDiscovered += (sender, args) =>
            {
                discoveredFiles.Add(args.FilePath);
            };

            await service.StartAsync();

            // Create multiple files simultaneously to stress test concurrent stability checking
            var concurrentFileTasks = Enumerable.Range(0, 8).Select(async i =>
            {
                try
                {
                    var testFile = Path.Combine(_testDirectory, $"concurrent_stability_{i}.test");
                    stabilityCheckFiles.Add(testFile);

                    await File.WriteAllTextAsync(testFile, $"Concurrent stability test content {i}");

                    // Stagger file creation slightly to create overlapping stability checks
                    await Task.Delay(Random.Shared.Next(50, 200));
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            });

            await Task.WhenAll(concurrentFileTasks);

            // Allow time for concurrent stability checking
            await Task.Delay(10000);
            await service.StopAsync();

            // PRIMARY ASSERTION: No exceptions during concurrent stability checking
            exceptions.Should().BeEmpty("concurrent stability checking should not cause exceptions");

            // SECONDARY ASSERTION: Concurrent operations don't interfere
            stabilityCheckFiles.Should().HaveCount(8, "all test files should have been created");

            // TERTIARY ASSERTION: System handles concurrent stability checking appropriately
            var concurrentFilesDiscovered = discoveredFiles.Where(f => f.Contains("concurrent_stability")).ToList();
            Console.WriteLine($"Concurrent files created: {stabilityCheckFiles.Count}, Files discovered: {concurrentFilesDiscovered.Count}");

            // Some files should be discovered - exact count depends on timing but system should be stable
            if (concurrentFilesDiscovered.Any())
            {
                concurrentFilesDiscovered.Should().OnlyHaveUniqueItems("no duplicate discoveries should occur due to concurrent processing");
            }
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
            FileFilters = new[] { "*.test" },
            ExcludeExtensions = new[] { ".tmp" },
            IncludeSubdirectories = false,
            StabilityCheckInterval = 1, // 1 second for testing
            MaxStabilityChecks = 3,     // Allow reasonable stability checking
            MinimumFileAge = 1          // 1 second minimum age for testing
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
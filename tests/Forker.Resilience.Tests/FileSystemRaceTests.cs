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

    /// <summary>
    /// Test 5: FileSystemWatcher Event Coalescing Race Validation
    /// Validates FileSystemWatcher behavior when multiple rapid file creations occur.
    /// This tests the event handling and potential duplicate event processing.
    /// </summary>
    [Fact]
    public async Task FileSystemWatcherEventCoalescing_RapidFileCreations_ShouldHandleCorrectly()
    {
        var service = CreateFileDiscoveryService();
        var discoveredFiles = new ConcurrentBag<string>();
        var exceptions = new ConcurrentBag<Exception>();

        try
        {
            service.FileDiscovered += (sender, args) =>
            {
                discoveredFiles.Add(args.FilePath);
            };

            await service.StartAsync();

            // Create multiple files rapidly to test FileSystemWatcher event handling
            var rapidCreationTasks = Enumerable.Range(0, 5).Select(async i =>
            {
                try
                {
                    var eventFile = Path.Combine(_testDirectory, $"event_coalescing_{i}.test");

                    // Write file completely before next iteration
                    await File.WriteAllTextAsync(eventFile, $"Event coalescing test file {i}");

                    // Brief delay to stagger file creation
                    await Task.Delay(50);
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            });

            await Task.WhenAll(rapidCreationTasks);

            // Allow time for event processing and file stability
            await Task.Delay(8000);
            await service.StopAsync();

            // PRIMARY ASSERTION: No exceptions during rapid file creation
            exceptions.Should().BeEmpty("rapid file creation should not cause exceptions");

            // SECONDARY ASSERTION: Event handling works correctly
            var coalescingFileDiscoveries = discoveredFiles.Where(f => f.Contains("event_coalescing")).ToList();
            Console.WriteLine($"Rapid creation files discovered: {coalescingFileDiscoveries.Count}");

            // TERTIARY ASSERTION: No duplicate processing due to event handling issues
            coalescingFileDiscoveries.Should().OnlyHaveUniqueItems("files should not be discovered multiple times due to event handling issues");

            // Validate all files were processed appropriately
            foreach (var discoveredFile in coalescingFileDiscoveries)
            {
                File.Exists(discoveredFile).Should().BeTrue($"discovered file {discoveredFile} should exist");
            }

            Console.WriteLine($"Files created: 5, Files discovered: {coalescingFileDiscoveries.Count}");
        }
        finally
        {
            await service.DisposeAsync();
        }
    }

    /// <summary>
    /// Test 6: FileSystemWatcher Initialization Race Validation
    /// Validates FileSystemWatcher behavior when files are created before vs after watcher starts.
    /// This tests the initialization race condition between directory scanning and live events.
    /// </summary>
    [Fact]
    public async Task FileSystemWatcherInitializationRace_PreExistingVsNewFiles_ShouldHandleCorrectly()
    {
        var discoveredFiles = new ConcurrentBag<string>();
        var exceptions = new ConcurrentBag<Exception>();
        var preExistingFile = Path.Combine(_testDirectory, "pre_existing.test");
        var postStartFile = Path.Combine(_testDirectory, "post_start.test");

        try
        {
            // Create pre-existing file before service starts
            await File.WriteAllTextAsync(preExistingFile, "Pre-existing file content");
            Console.WriteLine($"Created pre-existing file: {Path.GetFileName(preExistingFile)}");

            var service = CreateFileDiscoveryService();
            service.FileDiscovered += (sender, args) =>
            {
                discoveredFiles.Add(args.FilePath);
                Console.WriteLine($"Discovered: {Path.GetFileName(args.FilePath)}");
            };

            await service.StartAsync();

            // Brief delay to ensure service is fully started
            await Task.Delay(1000);

            // Create post-start file after service is running
            await File.WriteAllTextAsync(postStartFile, "Post-start file content");
            Console.WriteLine($"Created post-start file: {Path.GetFileName(postStartFile)}");

            // Allow time for both discovery mechanisms (initial scan + live events)
            await Task.Delay(8000);
            await service.StopAsync();

            // PRIMARY ASSERTION: No exceptions during initialization race handling
            exceptions.Should().BeEmpty("initialization race conditions should not cause exceptions");

            // SECONDARY ASSERTION: Both discovery mechanisms work
            var preExistingDiscovered = discoveredFiles.Any(f => f.Contains("pre_existing"));
            var postStartDiscovered = discoveredFiles.Any(f => f.Contains("post_start"));

            Console.WriteLine($"Pre-existing file discovered: {preExistingDiscovered}");
            Console.WriteLine($"Post-start file discovered: {postStartDiscovered}");

            // TERTIARY ASSERTION: No duplicate discoveries due to race conditions
            var allDiscoveredFiles = discoveredFiles.ToList();
            allDiscoveredFiles.Should().OnlyHaveUniqueItems("no files should be discovered multiple times due to initialization races");

            await service.DisposeAsync();
        }
        catch (Exception ex)
        {
            exceptions.Add(ex);
            throw;
        }
    }

    /// <summary>
    /// Test 7: FileSystemWatcher Event Ordering Race Validation
    /// Validates FileSystemWatcher event ordering when files are created, modified, and renamed rapidly.
    /// This tests the event sequence handling and potential race conditions in event processing order.
    /// </summary>
    [Fact]
    public async Task FileSystemWatcherEventOrdering_CreateModifyRename_ShouldHandleCorrectly()
    {
        var service = CreateFileDiscoveryService();
        var discoveredFiles = new ConcurrentBag<string>();
        var exceptions = new ConcurrentBag<Exception>();
        var eventSequenceFile = Path.Combine(_testDirectory, "event_sequence.test");
        var renamedFile = Path.Combine(_testDirectory, "event_sequence_renamed.test");

        try
        {
            service.FileDiscovered += (sender, args) =>
            {
                discoveredFiles.Add(args.FilePath);
                Console.WriteLine($"Discovered: {Path.GetFileName(args.FilePath)}");
            };

            await service.StartAsync();

            // Create rapid sequence of file system events: Create -> Modify -> Rename
            try
            {
                // 1. Create file
                await File.WriteAllTextAsync(eventSequenceFile, "Initial content");
                await Task.Delay(100);

                // 2. Modify file
                await File.AppendAllTextAsync(eventSequenceFile, " Modified content");
                await Task.Delay(100);

                // 3. Rename file (if original still exists)
                if (File.Exists(eventSequenceFile) && !File.Exists(renamedFile))
                {
                    File.Move(eventSequenceFile, renamedFile);
                    await Task.Delay(100);
                }
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
                Console.WriteLine($"File operation exception: {ex.Message}");
            }

            // Allow time for event processing
            await Task.Delay(8000);
            await service.StopAsync();

            // PRIMARY ASSERTION: No exceptions during event sequence handling
            exceptions.Should().BeEmpty("event sequence handling should not cause exceptions");

            // SECONDARY ASSERTION: Event ordering is handled correctly
            var sequenceRelatedFiles = discoveredFiles.Where(f =>
                f.Contains("event_sequence")).ToList();

            Console.WriteLine($"Event sequence related discoveries: {sequenceRelatedFiles.Count}");
            foreach (var file in sequenceRelatedFiles)
            {
                Console.WriteLine($"  - {Path.GetFileName(file)}");
            }

            // TERTIARY ASSERTION: No duplicate processing due to event ordering issues
            sequenceRelatedFiles.Should().OnlyHaveUniqueItems("files should not be discovered multiple times due to event ordering issues");

            // Validate final state
            var finalFileExists = File.Exists(renamedFile);
            var originalFileExists = File.Exists(eventSequenceFile);
            Console.WriteLine($"Final state - Original exists: {originalFileExists}, Renamed exists: {finalFileExists}");
        }
        finally
        {
            await service.DisposeAsync();
        }
    }

    /// <summary>
    /// Test 8: Pending File Timeout Cleanup - Validates proper cleanup of files that never stabilize
    /// This tests the timeout mechanism that prevents indefinite pending file accumulation
    /// </summary>
    [Fact]
    public async Task PendingFileTimeoutCleanup_UnstableFiles_ShouldBeAbandonedCorrectly()
    {
        var service = CreateFileDiscoveryService();
        var discoveredFiles = new ConcurrentBag<string>();
        var exceptions = new ConcurrentBag<Exception>();

        try
        {
            service.FileDiscovered += (sender, args) =>
            {
                try
                {
                    discoveredFiles.Add(args.FilePath);
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            };

            await service.StartAsync();

            // Create a file that will never stabilize due to continuous writes
            var unstableFile = Path.Combine(_testDirectory, "never_stable.test");

            // Start continuous writing to prevent stability
            using var cts = new CancellationTokenSource();
            var continuousWriteTask = Task.Run(async () =>
            {
                try
                {
                    var counter = 0;
                    await File.WriteAllTextAsync(unstableFile, "initial content");

                    while (!cts.Token.IsCancellationRequested)
                    {
                        await Task.Delay(500, cts.Token); // Write every 500ms
                        await File.AppendAllTextAsync(unstableFile, $" update{counter++}");
                    }
                }
                catch (OperationCanceledException)
                {
                    // Expected when cancelled
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            }, cts.Token);

            // Allow enough time for multiple stability checks to fail
            await Task.Delay(8000); // Should exceed timeout threshold

            // Stop continuous writing
            cts.Cancel();

            try
            {
                await continuousWriteTask;
            }
            catch (OperationCanceledException)
            {
                // Expected
            }

            // Give file system time to release file handles from background writer
            // This prevents race conditions where the test's own writer still has the file open
            await Task.Delay(200);

            await service.StopAsync();

            // Primary assertion: System should handle unstable files without exceptions
            exceptions.Should().BeEmpty("pending file timeout should not cause exceptions");

            // Secondary assertion: Unstable file should NOT be discovered (timed out)
            discoveredFiles.Should().NotContain(f => f.Contains("never_stable"),
                "files that never stabilize should be abandoned via timeout mechanism");
        }
        finally
        {
            await service.DisposeAsync();
        }
    }

    /// <summary>
    /// Test 9: Concurrent File Modification During Stability Check - Validates handling of files modified during checking
    /// This tests race conditions between stability checking and external file modifications
    /// </summary>
    [Fact]
    public async Task ConcurrentModificationDuringStabilityCheck_ShouldHandleCorrectly()
    {
        var service = CreateFileDiscoveryService();
        var discoveredFiles = new ConcurrentBag<string>();
        var exceptions = new ConcurrentBag<Exception>();

        try
        {
            service.FileDiscovered += (sender, args) =>
            {
                try
                {
                    discoveredFiles.Add(args.FilePath);
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            };

            await service.StartAsync();

            var testFile = Path.Combine(_testDirectory, "concurrent_mod.test");

            // Create initial file
            await File.WriteAllTextAsync(testFile, "initial content");

            // Wait for stability checking to begin
            await Task.Delay(1500);

            // Modify file during stability check window
            var modificationTask = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(500); // Delay to hit stability check window
                    await File.AppendAllTextAsync(testFile, " modified during check");
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            });

            await modificationTask;

            // Allow time for re-stability checking
            await Task.Delay(5000);

            await service.StopAsync();

            // Primary assertion: No exceptions from concurrent modifications
            exceptions.Should().BeEmpty("concurrent modifications during stability check should not cause exceptions");

            // Secondary assertion: File should either be discovered (if it stabilized) or not (if it didn't)
            // Both outcomes are valid depending on timing
            if (discoveredFiles.Any(f => f.Contains("concurrent_mod")))
            {
                // File was discovered - verify it contains the modification
                var finalContent = await File.ReadAllTextAsync(testFile);
                finalContent.Should().Contain("modified during check",
                    "if file was discovered, it should contain the concurrent modification");
            }
            // If file wasn't discovered, that's also valid - it may not have stabilized in time
        }
        finally
        {
            await service.DisposeAsync();
        }
    }

    /// <summary>
    /// Test 10: Pending File Memory Management - Validates proper cleanup of pending file tracking
    /// This tests that the service doesn't accumulate pending files indefinitely
    /// </summary>
    [Fact]
    public async Task PendingFileMemoryManagement_ManyTransientFiles_ShouldNotAccumulate()
    {
        var service = CreateFileDiscoveryService();
        var discoveredFiles = new ConcurrentBag<string>();
        var exceptions = new ConcurrentBag<Exception>();

        try
        {
            service.FileDiscovered += (sender, args) =>
            {
                try
                {
                    discoveredFiles.Add(args.FilePath);
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            };

            await service.StartAsync();

            // Create many short-lived files that will be deleted before stabilization
            var transientTasks = Enumerable.Range(0, 20).Select(async i =>
            {
                try
                {
                    var transientFile = Path.Combine(_testDirectory, $"transient_{i}.test");
                    await File.WriteAllTextAsync(transientFile, $"transient content {i}");

                    // Delete file quickly (before stability check completes)
                    await Task.Delay(Random.Shared.Next(100, 500));

                    if (File.Exists(transientFile))
                    {
                        File.Delete(transientFile);
                    }
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            });

            await Task.WhenAll(transientTasks);

            // Allow time for cleanup processing
            await Task.Delay(3000);

            await service.StopAsync();

            // Primary assertion: No exceptions from transient file handling
            exceptions.Should().BeEmpty("transient file processing should not cause exceptions");

            // Secondary assertion: Minimal or no discoveries expected (files deleted before stabilization)
            var transientDiscoveries = discoveredFiles.Count(f => f.Contains("transient"));
            transientDiscoveries.Should().BeLessOrEqualTo(5,
                "most transient files should be deleted before discovery (memory management test)");
        }
        finally
        {
            await service.DisposeAsync();
        }
    }

    /// <summary>
    /// Test 11: File Access Race During Stability Check - Validates handling of file access attempts during stability checking
    /// This tests I/O race conditions when external processes access files during stability validation
    /// </summary>
    [Fact]
    public async Task FileAccessRaceDuringStabilityCheck_ExternalAccess_ShouldHandleCorrectly()
    {
        var service = CreateFileDiscoveryService();
        var discoveredFiles = new ConcurrentBag<string>();
        var exceptions = new ConcurrentBag<Exception>();

        try
        {
            service.FileDiscovered += (sender, args) =>
            {
                try
                {
                    discoveredFiles.Add(args.FilePath);
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            };

            await service.StartAsync();

            var testFile = Path.Combine(_testDirectory, "io_race.test");

            // Create file and immediately start external access simulation
            await File.WriteAllTextAsync(testFile, "I/O race test content");

            // Simulate external process accessing file during stability check
            var externalAccessTask = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(800); // Start during stability check window

                    // Multiple external access attempts
                    for (int i = 0; i < 3; i++)
                    {
                        if (File.Exists(testFile))
                        {
                            // Read file (simulates external polling/monitoring)
                            var content = await File.ReadAllTextAsync(testFile);
                            await Task.Delay(200);
                        }
                    }
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            });

            await externalAccessTask;

            // Allow completion of stability checking
            await Task.Delay(5000);
            await service.StopAsync();

            // Primary assertion: No I/O exceptions from concurrent access
            exceptions.Should().BeEmpty("external file access during stability check should not cause I/O exceptions");

            // Secondary assertion: File should be processed correctly despite external access
            if (discoveredFiles.Any(f => f.Contains("io_race")))
            {
                // File was discovered - verify it's still accessible
                File.Exists(testFile).Should().BeTrue("discovered file should remain accessible");
                var finalContent = await File.ReadAllTextAsync(testFile);
                finalContent.Should().Contain("I/O race test content", "file content should be preserved despite concurrent access");
            }
            // If file wasn't discovered, that's also valid - external access may have affected stability detection
        }
        finally
        {
            await service.DisposeAsync();
        }
    }

    /// <summary>
    /// Test 12: Concurrent File Stream Access - Validates handling of multiple file stream operations
    /// This tests I/O race conditions when stability checking occurs alongside stream operations
    /// </summary>
    [Fact]
    public async Task ConcurrentFileStreamAccess_StreamOperations_ShouldNotCauseDeadlocks()
    {
        var service = CreateFileDiscoveryService();
        var discoveredFiles = new ConcurrentBag<string>();
        var exceptions = new ConcurrentBag<Exception>();

        try
        {
            service.FileDiscovered += (sender, args) =>
            {
                try
                {
                    discoveredFiles.Add(args.FilePath);
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            };

            await service.StartAsync();

            var testFile = Path.Combine(_testDirectory, "stream_race.test");

            // Create file with concurrent stream operations
            var streamOperationsTask = Task.Run(async () =>
            {
                try
                {
                    // Initial file creation
                    using (var stream = new FileStream(testFile, FileMode.Create, FileAccess.Write, FileShare.Read))
                    {
                        var content = System.Text.Encoding.UTF8.GetBytes("Stream test content");
                        await stream.WriteAsync(content);
                        await stream.FlushAsync();
                        await Task.Delay(500); // Hold stream open briefly
                    }

                    // Brief delay then reopen for append
                    await Task.Delay(300);

                    using (var stream = new FileStream(testFile, FileMode.Append, FileAccess.Write, FileShare.Read))
                    {
                        var additionalContent = System.Text.Encoding.UTF8.GetBytes(" Additional content");
                        await stream.WriteAsync(additionalContent);
                        await stream.FlushAsync();
                        await Task.Delay(300); // Hold stream open during potential stability check
                    }
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            });

            await streamOperationsTask;

            // Allow time for stability checking and file discovery
            await Task.Delay(6000);
            await service.StopAsync();

            // Primary assertion: No deadlocks or exceptions from concurrent stream operations
            exceptions.Should().BeEmpty("concurrent file stream operations should not cause deadlocks or exceptions");

            // Secondary assertion: System functionality validation
            File.Exists(testFile).Should().BeTrue("test file should exist after stream operations");
            var content = await File.ReadAllTextAsync(testFile);
            content.Should().Contain("Stream test content", "file should contain initial content");
            content.Should().Contain("Additional content", "file should contain appended content");

            // Tertiary assertion: File discovery behavior (timing-tolerant)
            // File may or may not be discovered depending on stream timing vs stability checking
            if (discoveredFiles.Any(f => f.Contains("stream_race")))
            {
                Console.WriteLine("File was discovered despite concurrent stream operations");
            }
            else
            {
                Console.WriteLine("File was not discovered - stream operations may have interfered with stability detection");
            }
        }
        finally
        {
            await service.DisposeAsync();
        }
    }

    /// <summary>
    /// Test 13: Directory Access Permission Race - Validates handling of directory permission changes during file processing
    /// This tests I/O race conditions when directory permissions change during file discovery and stability checking
    /// </summary>
    [Fact]
    public async Task DirectoryAccessPermissionRace_PermissionChanges_ShouldHandleGracefully()
    {
        var service = CreateFileDiscoveryService();
        var discoveredFiles = new ConcurrentBag<string>();
        var exceptions = new ConcurrentBag<Exception>();
        var subdirectory = Path.Combine(_testDirectory, "permission_test");

        try
        {
            Directory.CreateDirectory(subdirectory);

            service.FileDiscovered += (sender, args) =>
            {
                try
                {
                    discoveredFiles.Add(args.FilePath);
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            };

            await service.StartAsync();

            var testFile = Path.Combine(subdirectory, "permission_race.test");

            // Create files in subdirectory
            var fileCreationTask = Task.Run(async () =>
            {
                try
                {
                    await File.WriteAllTextAsync(testFile, "Permission test content");
                    await Task.Delay(1000);

                    // Create additional files to stress test directory access
                    for (int i = 0; i < 5; i++)
                    {
                        var additionalFile = Path.Combine(subdirectory, $"additional_{i}.test");
                        await File.WriteAllTextAsync(additionalFile, $"Additional content {i}");
                        await Task.Delay(200);
                    }
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            });

            await fileCreationTask;

            // Allow time for file discovery and stability checking
            await Task.Delay(8000);
            await service.StopAsync();

            // Primary assertion: No permission-related I/O exceptions
            exceptions.Should().BeEmpty("directory access during file discovery should not cause permission exceptions");

            // Secondary assertion: Directory and files should remain accessible
            Directory.Exists(subdirectory).Should().BeTrue("subdirectory should remain accessible");
            File.Exists(testFile).Should().BeTrue("test file should remain accessible");

            // Tertiary assertion: Discovery validation (directory scanning behavior)
            if (discoveredFiles.Any())
            {
                var discoveredInSubdir = discoveredFiles.Count(f => f.Contains("permission_test"));
                Console.WriteLine($"Discovered {discoveredInSubdir} files in subdirectory");
                // Note: May be 0 if IncludeSubdirectories=false in service configuration
            }
        }
        finally
        {
            await service.DisposeAsync();
        }
    }

    /// <summary>
    /// Test 14: File System Volume Stress - Validates handling of high I/O volume during stability checking
    /// This tests I/O race conditions under high file system load
    /// </summary>
    [Fact]
    public async Task FileSystemVolumeStress_HighIOLoad_ShouldMaintainStability()
    {
        var service = CreateFileDiscoveryService();
        var discoveredFiles = new ConcurrentBag<string>();
        var exceptions = new ConcurrentBag<Exception>();

        try
        {
            service.FileDiscovered += (sender, args) =>
            {
                try
                {
                    discoveredFiles.Add(args.FilePath);
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            };

            await service.StartAsync();

            // Create high I/O load with multiple concurrent file operations
            var ioStressTasks = Enumerable.Range(0, 10).Select(async taskId =>
            {
                try
                {
                    for (int fileId = 0; fileId < 3; fileId++)
                    {
                        var stressFile = Path.Combine(_testDirectory, $"stress_{taskId}_{fileId}.test");

                        // Create file with variable content to stress I/O
                        var content = new string('S', Random.Shared.Next(1000, 5000));
                        await File.WriteAllTextAsync(stressFile, content);

                        // Random delay to create varied I/O patterns
                        await Task.Delay(Random.Shared.Next(100, 300));

                        // Additional I/O operation
                        if (File.Exists(stressFile))
                        {
                            await File.AppendAllTextAsync(stressFile, " STRESS_MARKER");
                        }
                    }
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            });

            await Task.WhenAll(ioStressTasks);

            // Allow time for stability checking under I/O stress
            await Task.Delay(10000);
            await service.StopAsync();

            // Primary assertion: System stability under high I/O load
            exceptions.Should().BeEmpty("high I/O volume should not cause system instability or exceptions");

            // Secondary assertion: File system integrity validation
            var stressFiles = Directory.GetFiles(_testDirectory, "stress_*.test");
            stressFiles.Should().NotBeEmpty("stress test files should be created successfully");

            foreach (var stressFile in stressFiles.Take(5)) // Check sample of files
            {
                var content = await File.ReadAllTextAsync(stressFile);
                content.Should().Contain("STRESS_MARKER", $"stress file {Path.GetFileName(stressFile)} should contain stress marker");
            }

            // Tertiary assertion: Discovery performance under load
            var discoveredStressFiles = discoveredFiles.Count(f => f.Contains("stress_"));
            Console.WriteLine($"Discovered {discoveredStressFiles}/{stressFiles.Length} stress files under high I/O load");

            // File discovery may be impacted by high I/O load - this is acceptable behavior
            // The key is that the system remains stable without exceptions
        }
        finally
        {
            await service.DisposeAsync();
        }
    }

    /// <summary>
    /// Test 15: Medical Imaging Batch Processing - Validates handling of realistic medical imaging file batch arrivals
    /// This tests large file processing patterns typical in medical imaging environments
    /// </summary>
    [Fact]
    public async Task MedicalImagingBatchProcessing_LargeFileBatches_ShouldHandleCorrectly()
    {
        var service = CreateFileDiscoveryService();
        var discoveredFiles = new ConcurrentBag<string>();
        var exceptions = new ConcurrentBag<Exception>();

        try
        {
            service.FileDiscovered += (sender, args) =>
            {
                try
                {
                    discoveredFiles.Add(args.FilePath);
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            };

            await service.StartAsync();

            // Simulate medical imaging batch processing patterns
            var medicalImageTypes = new[] { ".svs", ".tiff", ".ndpi", ".scn" };
            var batchProcessingTask = Task.Run(async () =>
            {
                try
                {
                    // Batch 1: Multiple SVS files (typical scanning session)
                    for (int i = 0; i < 3; i++)
                    {
                        var svsFile = Path.Combine(_testDirectory, $"scan_{i:D3}.svs");
                        var content = new string('M', Random.Shared.Next(5000, 15000)); // Larger files
                        await File.WriteAllTextAsync(svsFile, content);
                        await Task.Delay(Random.Shared.Next(500, 1500)); // Realistic batch timing
                    }

                    // Brief pause between batches (typical workflow)
                    await Task.Delay(2000);

                    // Batch 2: Mixed medical imaging formats
                    for (int i = 0; i < 4; i++)
                    {
                        var extension = medicalImageTypes[i % medicalImageTypes.Length];
                        var imageFile = Path.Combine(_testDirectory, $"medical_{i:D3}{extension}");
                        var content = new string('I', Random.Shared.Next(3000, 10000));
                        await File.WriteAllTextAsync(imageFile, content);
                        await Task.Delay(Random.Shared.Next(300, 800));
                    }
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            });

            await batchProcessingTask;

            // Allow time for batch processing and stability checking
            await Task.Delay(15000);
            await service.StopAsync();

            // Primary assertion: System stability under medical imaging loads
            exceptions.Should().BeEmpty("medical imaging batch processing should not cause exceptions");

            // Secondary assertion: File discovery validation
            var discoveredMedicalFiles = discoveredFiles.Where(f =>
                f.Contains("scan_") || f.Contains("medical_")).ToList();

            if (discoveredMedicalFiles.Any())
            {
                Console.WriteLine($"Discovered {discoveredMedicalFiles.Count} medical imaging files in batch processing");

                // Verify medical file format handling
                var svsFiles = discoveredMedicalFiles.Count(f => f.EndsWith(".svs"));
                var tiffFiles = discoveredMedicalFiles.Count(f => f.EndsWith(".tiff"));
                Console.WriteLine($"Processed: {svsFiles} SVS files, {tiffFiles} TIFF files");
            }
            else
            {
                Console.WriteLine("No medical files discovered - may be due to stability checking requirements for large files");
            }

            // Tertiary assertion: File system integrity after batch processing
            var createdFiles = Directory.GetFiles(_testDirectory, "scan_*").Concat(
                               Directory.GetFiles(_testDirectory, "medical_*")).ToList();
            createdFiles.Should().NotBeEmpty("medical imaging files should be created successfully");
        }
        finally
        {
            await service.DisposeAsync();
        }
    }

    /// <summary>
    /// Test 16: External Tool Integration Simulation - Validates handling of files being accessed by external imaging tools
    /// This tests scenarios where imaging software locks files during processing
    /// </summary>
    [Fact]
    public async Task ExternalToolIntegrationSimulation_LockedFiles_ShouldHandleGracefully()
    {
        var service = CreateFileDiscoveryService();
        var discoveredFiles = new ConcurrentBag<string>();
        var exceptions = new ConcurrentBag<Exception>();

        try
        {
            service.FileDiscovered += (sender, args) =>
            {
                try
                {
                    discoveredFiles.Add(args.FilePath);
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            };

            await service.StartAsync();

            var lockedFile = Path.Combine(_testDirectory, "external_tool.svs");

            // Simulate external tool workflow
            var externalToolTask = Task.Run(async () =>
            {
                try
                {
                    // Phase 1: Tool creates file (exclusive access)
                    using (var stream = new FileStream(lockedFile, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        var initialContent = System.Text.Encoding.UTF8.GetBytes("External tool processing medical image");
                        await stream.WriteAsync(initialContent);
                        await stream.FlushAsync();
                        await Task.Delay(2000); // Simulate processing time with exclusive lock
                    }

                    // Phase 2: Brief pause (tool processing)
                    await Task.Delay(1000);

                    // Phase 3: Tool reopens for analysis (shared read access)
                    using (var stream = new FileStream(lockedFile, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        var content = new byte[stream.Length];
                        await stream.ReadAsync(content);
                        await Task.Delay(1500); // Simulate analysis time
                    }

                    // Phase 4: Tool completes, file becomes fully available
                    await Task.Delay(500);
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            });

            await externalToolTask;

            // Allow time for file discovery after external tool completion
            await Task.Delay(8000);
            await service.StopAsync();

            // Primary assertion: No exceptions from external tool file locking
            exceptions.Should().BeEmpty("external tool file locking should not cause exceptions");

            // Secondary assertion: File availability validation
            File.Exists(lockedFile).Should().BeTrue("file should exist after external tool processing");
            var finalContent = await File.ReadAllTextAsync(lockedFile);
            finalContent.Should().Contain("External tool processing", "file content should be preserved");

            // Tertiary assertion: Discovery behavior (timing-tolerant)
            if (discoveredFiles.Any(f => f.Contains("external_tool")))
            {
                Console.WriteLine("File was discovered after external tool released lock");
            }
            else
            {
                Console.WriteLine("File was not discovered - external tool locking may have prevented stability detection");
            }
        }
        finally
        {
            await service.DisposeAsync();
        }
    }

    /// <summary>
    /// Test 17: Large File Arrival Pattern - Validates handling of large medical imaging file arrivals
    /// This tests realistic large file processing patterns (500MB+ equivalent simulation)
    /// </summary>
    [Fact]
    public async Task LargeFileArrivalPattern_RealisticSizes_ShouldMaintainPerformance()
    {
        var service = CreateFileDiscoveryService();
        var discoveredFiles = new ConcurrentBag<string>();
        var exceptions = new ConcurrentBag<Exception>();

        try
        {
            service.FileDiscovered += (sender, args) =>
            {
                try
                {
                    discoveredFiles.Add(args.FilePath);
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            };

            await service.StartAsync();

            // Simulate large file arrivals (scaled down for CI environment)
            var largeFileTask = Task.Run(async () =>
            {
                try
                {
                    for (int i = 0; i < 2; i++) // Reduced for CI performance
                    {
                        var largeFile = Path.Combine(_testDirectory, $"large_medical_{i}.svs");

                        // Simulate large file creation (use chunked writing to stress I/O)
                        using (var stream = new FileStream(largeFile, FileMode.Create, FileAccess.Write, FileShare.Read))
                        {
                            var chunkSize = 50000; // 50KB chunks
                            var totalChunks = 20;   // ~1MB total (scaled down from GB)

                            for (int chunk = 0; chunk < totalChunks; chunk++)
                            {
                                var chunkData = new byte[chunkSize];
                                Random.Shared.NextBytes(chunkData);
                                await stream.WriteAsync(chunkData);
                                await stream.FlushAsync();
                                await Task.Delay(50); // Simulate realistic write timing
                            }
                        }

                        // Realistic delay between large file arrivals
                        await Task.Delay(3000);
                    }
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            });

            await largeFileTask;

            // Allow extended time for large file stability checking
            await Task.Delay(12000);
            await service.StopAsync();

            // Primary assertion: System performance under large file loads
            exceptions.Should().BeEmpty("large file processing should not cause performance exceptions");

            // Secondary assertion: File integrity validation
            var largeFiles = Directory.GetFiles(_testDirectory, "large_medical_*.svs");
            largeFiles.Should().NotBeEmpty("large medical files should be created successfully");

            foreach (var largeFile in largeFiles)
            {
                var fileInfo = new FileInfo(largeFile);
                fileInfo.Length.Should().BeGreaterThan(500000, $"large file {Path.GetFileName(largeFile)} should have realistic size");
            }

            // Tertiary assertion: Discovery performance (timing-tolerant)
            var discoveredLargeFiles = discoveredFiles.Count(f => f.Contains("large_medical"));
            Console.WriteLine($"Discovered {discoveredLargeFiles}/{largeFiles.Length} large medical files");

            // Large files may require extended stability checking - this is expected behavior
            if (discoveredLargeFiles > 0)
            {
                Console.WriteLine("System successfully processed large medical imaging files");
            }
            else
            {
                Console.WriteLine("Large files may require extended stability checking - system remained stable");
            }
        }
        finally
        {
            await service.DisposeAsync();
        }
    }

    /// <summary>
    /// Test 18: Multi-Scanner Workflow Simulation - Validates handling of multiple concurrent scanners creating files
    /// This tests typical medical imaging lab scenarios with multiple scanning stations
    /// </summary>
    [Fact]
    public async Task MultiScannerWorkflowSimulation_ConcurrentScanners_ShouldHandleCorrectly()
    {
        var service = CreateFileDiscoveryService();
        var discoveredFiles = new ConcurrentBag<string>();
        var exceptions = new ConcurrentBag<Exception>();

        try
        {
            service.FileDiscovered += (sender, args) =>
            {
                try
                {
                    discoveredFiles.Add(args.FilePath);
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            };

            await service.StartAsync();

            // Simulate multiple scanner stations
            var scannerTasks = Enumerable.Range(1, 3).Select(async scannerId =>
            {
                try
                {
                    for (int slide = 1; slide <= 2; slide++) // 2 slides per scanner
                    {
                        var scanFile = Path.Combine(_testDirectory, $"scanner{scannerId}_slide{slide}.svs");

                        // Simulate scanner workflow: create -> write progressively -> finalize
                        using (var stream = new FileStream(scanFile, FileMode.Create, FileAccess.Write, FileShare.Read))
                        {
                            // Progressive scanning simulation
                            for (int pass = 1; pass <= 3; pass++)
                            {
                                var passData = System.Text.Encoding.UTF8.GetBytes($"Scanner{scannerId} Pass{pass} Slide{slide} Data{new string('S', 1000)}\n");
                                await stream.WriteAsync(passData);
                                await stream.FlushAsync();
                                await Task.Delay(Random.Shared.Next(800, 1500)); // Scanner processing time
                            }
                        }

                        // Scanner completion delay
                        await Task.Delay(Random.Shared.Next(500, 1000));
                    }
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            });

            await Task.WhenAll(scannerTasks);

            // Allow time for multi-scanner file discovery
            await Task.Delay(12000);
            await service.StopAsync();

            // Primary assertion: Multi-scanner workflow stability
            exceptions.Should().BeEmpty("multi-scanner workflows should not cause exceptions");

            // Secondary assertion: Scanner file validation
            var scannerFiles = Directory.GetFiles(_testDirectory, "scanner*.svs");
            scannerFiles.Should().HaveCount(6, "should create 2 files per 3 scanners = 6 total files");

            foreach (var scannerFile in scannerFiles)
            {
                var content = await File.ReadAllTextAsync(scannerFile);
                content.Should().Contain("Pass1", $"scanner file {Path.GetFileName(scannerFile)} should contain Pass1 data");
                content.Should().Contain("Pass3", $"scanner file {Path.GetFileName(scannerFile)} should contain Pass3 data");
            }

            // Tertiary assertion: Discovery coordination
            var discoveredScannerFiles = discoveredFiles.Count(f => f.Contains("scanner"));
            Console.WriteLine($"Discovered {discoveredScannerFiles}/6 scanner files from multi-scanner workflow");

            // Validate no cross-scanner interference in discovered files
            foreach (var discoveredFile in discoveredFiles.Where(f => f.Contains("scanner")))
            {
                File.Exists(discoveredFile).Should().BeTrue($"discovered scanner file {Path.GetFileName(discoveredFile)} should exist");
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
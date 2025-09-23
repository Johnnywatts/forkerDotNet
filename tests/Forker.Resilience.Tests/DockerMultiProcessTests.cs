using FluentAssertions;
using System.Diagnostics;

namespace Forker.Resilience.Tests;

/// <summary>
/// Docker-based multi-process race condition validation tests.
/// Tests race conditions across real process boundaries with shared file systems,
/// simulating production hospital network conditions for medical imaging workflows.
/// Phase 5.1B: Multi-Process Testing validates race condition fixes across process boundaries.
/// </summary>
public class DockerMultiProcessTests : IDisposable
{
    private readonly string _testResultsDirectory;
    private readonly string _dockerComposeFile;

    public DockerMultiProcessTests()
    {
        // Find the test source directory (not bin/Debug)
        var currentDir = Directory.GetCurrentDirectory();
        var testSourceDir = currentDir;

        // Navigate up from bin/Debug/net8.0 to the test project directory
        while (testSourceDir != null && !File.Exists(Path.Combine(testSourceDir, "docker-compose.yml")))
        {
            var parentDir = Directory.GetParent(testSourceDir);
            if (parentDir == null) break;
            testSourceDir = parentDir.FullName;
        }

        if (testSourceDir == null || !File.Exists(Path.Combine(testSourceDir, "docker-compose.yml")))
        {
            throw new FileNotFoundException("Could not find docker-compose.yml file in test directory hierarchy");
        }

        _testResultsDirectory = Path.Combine(testSourceDir, "test-results");
        _dockerComposeFile = Path.Combine(testSourceDir, "docker-compose.yml");
        Directory.CreateDirectory(_testResultsDirectory);
    }

    /// <summary>
    /// Core Docker Test: Multi-Process Race Condition Detection
    /// Validates that race condition fixes work across real process boundaries with shared file systems.
    /// This test simulates multiple ForkerDotNet instances running concurrently in a production environment.
    /// </summary>
    [Fact]
    public async Task MultiProcess_ConcurrentFileProcessing_ShouldNotCauseRaceConditions()
    {
        // Skip if Docker is not available
        if (!IsDockerAvailable())
        {
            throw new SkipException("Docker is not available for multi-process testing");
        }

        var containerName = "forker-race-test";
        var timeout = TimeSpan.FromMinutes(10);

        try
        {
            // Clean up any existing containers
            await RunDockerCommand($"container rm -f {containerName}", ignoreErrors: true);
            await RunDockerCommand("volume rm forker-resilience-tests_forker-shared", ignoreErrors: true);

            // Build and run the multi-process test
            var buildResult = await RunDockerCommand("compose -f docker-compose.yml build", timeout);
            buildResult.Success.Should().BeTrue("Docker build should succeed");

            var runResult = await RunDockerCommand("compose -f docker-compose.yml up --abort-on-container-exit forker-multiprocess-test", timeout);

            // Analyze test results
            var logs = await GetContainerLogs(containerName);
            AnalyzeMultiProcessResults(logs, runResult.Output);

            runResult.Success.Should().BeTrue("Multi-process race condition test should complete successfully");
        }
        finally
        {
            // Cleanup
            await RunDockerCommand("compose -f docker-compose.yml down -v", ignoreErrors: true);
        }
    }

    /// <summary>
    /// Extended Load Test: Sustained Multi-Process Processing
    /// Tests race conditions under sustained load with multiple processes and continuous file creation.
    /// </summary>
    [Fact]
    public async Task SustainedLoad_MultipleProcesses_ShouldMaintainDataIntegrity()
    {
        if (!IsDockerAvailable())
        {
            throw new SkipException("Docker is not available for sustained load testing");
        }

        var timeout = TimeSpan.FromMinutes(15);

        try
        {
            // Clean up
            await RunDockerCommand("compose -f docker-compose.yml down -v", ignoreErrors: true);

            // Run sustained load test with all services
            var buildResult = await RunDockerCommand("compose -f docker-compose.yml build", timeout);
            buildResult.Success.Should().BeTrue("Docker build should succeed");

            // Start all services for sustained testing
            var runResult = await RunDockerCommand("compose -f docker-compose.yml up --abort-on-container-exit --timeout 900", timeout);

            // Analyze comprehensive results
            var mainLogs = await GetContainerLogs("forker-race-test");
            var loadLogs = await GetContainerLogs("forker-load-gen");
            var monitorLogs = await GetContainerLogs("forker-monitor");

            AnalyzeSustainedLoadResults(mainLogs, loadLogs, monitorLogs, runResult.Output);

            runResult.Success.Should().BeTrue("Sustained multi-process load test should complete without race conditions");
        }
        finally
        {
            await RunDockerCommand("compose -f docker-compose.yml down -v", ignoreErrors: true);
        }
    }

    /// <summary>
    /// Network Partition Simulation Test
    /// Tests race conditions during network instability scenarios common in hospital environments.
    /// </summary>
    [Fact]
    public async Task NetworkPartition_RaceConditionResilience_ShouldMaintainConsistency()
    {
        if (!IsDockerAvailable())
        {
            throw new SkipException("Docker is not available for network partition testing");
        }

        var timeout = TimeSpan.FromMinutes(12);

        try
        {
            await RunDockerCommand("compose -f docker-compose.yml down -v", ignoreErrors: true);

            // Start services
            var buildResult = await RunDockerCommand("compose -f docker-compose.yml build", timeout);
            buildResult.Success.Should().BeTrue("Docker build should succeed");

            // Start in background
            var startResult = await RunDockerCommand("compose -f docker-compose.yml up -d", TimeSpan.FromMinutes(2));
            startResult.Success.Should().BeTrue("Services should start successfully");

            // Allow services to initialize
            await Task.Delay(10000);

            // Simulate network instability by pausing/unpausing containers
            for (int i = 0; i < 3; i++)
            {
                await Task.Delay(5000);
                await RunDockerCommand("container pause forker-race-test", ignoreErrors: true);
                await Task.Delay(2000);
                await RunDockerCommand("container unpause forker-race-test", ignoreErrors: true);
            }

            // Allow processing to complete
            await Task.Delay(15000);

            // Stop and analyze
            await RunDockerCommand("compose -f docker-compose.yml stop");

            var logs = await GetContainerLogs("forker-race-test");
            AnalyzeNetworkPartitionResults(logs);
        }
        finally
        {
            await RunDockerCommand("compose -f docker-compose.yml down -v", ignoreErrors: true);
        }
    }

    private bool IsDockerAvailable()
    {
        try
        {
            var result = RunDockerCommand("version", TimeSpan.FromSeconds(10)).Result;
            return result.Success;
        }
        catch
        {
            return false;
        }
    }

    private async Task<(bool Success, string Output, string Error)> RunDockerCommand(string arguments, TimeSpan? timeout = null, bool ignoreErrors = false)
    {
        timeout ??= TimeSpan.FromMinutes(5);

        var startInfo = new ProcessStartInfo
        {
            FileName = "docker",
            Arguments = arguments,
            WorkingDirectory = Path.GetDirectoryName(_dockerComposeFile) ?? Directory.GetCurrentDirectory(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo };

        var outputBuilder = new System.Text.StringBuilder();
        var errorBuilder = new System.Text.StringBuilder();

        process.OutputDataReceived += (_, e) => { if (e.Data != null) outputBuilder.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) errorBuilder.AppendLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var completed = await Task.Run(() => process.WaitForExit((int)timeout.Value.TotalMilliseconds));

        if (!completed)
        {
            process.Kill();
            if (!ignoreErrors)
            {
                throw new TimeoutException($"Docker command timed out: docker {arguments}");
            }
            return (false, "", "Timeout");
        }

        var output = outputBuilder.ToString();
        var error = errorBuilder.ToString();
        var success = process.ExitCode == 0;

        if (!success && !ignoreErrors)
        {
            throw new InvalidOperationException($"Docker command failed: docker {arguments}\nOutput: {output}\nError: {error}");
        }

        return (success, output, error);
    }

    private async Task<string> GetContainerLogs(string containerName)
    {
        try
        {
            var result = await RunDockerCommand($"logs {containerName}", TimeSpan.FromMinutes(1), ignoreErrors: true);
            return result.Output + "\n" + result.Error;
        }
        catch
        {
            return "";
        }
    }

    private void AnalyzeMultiProcessResults(string logs, string runOutput)
    {
        // Check for race condition indicators
        logs.Should().NotContain("RACE CONDITION", "multi-process testing should not detect race conditions");
        logs.Should().NotContain("Exception", "no exceptions should occur during multi-process testing");
        logs.Should().NotContain("Error", "no errors should occur during multi-process testing");

        // Verify successful completion
        logs.Should().Contain("SUCCESS: No race conditions detected", "multi-process validation should succeed");

        // Check file processing consistency
        if (logs.Contains("Source files:") && logs.Contains("Target A files:") && logs.Contains("Target B files:"))
        {
            // Extract file counts for validation
            var lines = logs.Split('\n');
            var sourceCount = ExtractFileCount(lines, "Source files:");
            var targetACount = ExtractFileCount(lines, "Target A files:");
            var targetBCount = ExtractFileCount(lines, "Target B files:");

            if (sourceCount > 0)
            {
                targetACount.Should().Be(sourceCount, "Target A should have same file count as source (no race conditions)");
                targetBCount.Should().Be(sourceCount, "Target B should have same file count as source (no race conditions)");
            }
        }
    }

    private void AnalyzeSustainedLoadResults(string mainLogs, string loadLogs, string monitorLogs, string runOutput)
    {
        // Analyze main process results
        AnalyzeMultiProcessResults(mainLogs, runOutput);

        // Check load generator operated correctly
        loadLogs.Should().Contain("Starting continuous load generation", "load generator should have started");

        // Check monitoring detected file operations
        monitorLogs.Should().Contain("Starting file system monitoring", "monitor should have started");

        // Verify no race conditions under sustained load
        var allLogs = mainLogs + loadLogs + monitorLogs;
        allLogs.Should().NotContain("RACE CONDITION", "sustained load should not cause race conditions");
        allLogs.Should().NotContain("duplicate", "sustained load should not cause duplicates");
    }

    private void AnalyzeNetworkPartitionResults(string logs)
    {
        // Network partition should not cause race conditions
        logs.Should().NotContain("RACE CONDITION", "network partition should not cause race conditions");
        logs.Should().NotContain("Exception", "network partition should be handled gracefully");

        // Service should recover properly
        logs.Should().Contain("Starting ForkerDotNet", "service should start successfully");
    }

    private int ExtractFileCount(string[] lines, string prefix)
    {
        var line = lines.FirstOrDefault(l => l.Contains(prefix));
        if (line == null) return 0;

        var parts = line.Split(':');
        if (parts.Length < 2) return 0;

        return int.TryParse(parts[1].Trim(), out var count) ? count : 0;
    }

    public void Dispose()
    {
        // Cleanup any remaining containers
        try
        {
            RunDockerCommand("compose -f docker-compose.yml down -v", ignoreErrors: true).Wait(TimeSpan.FromMinutes(2));
        }
        catch
        {
            // Ignore cleanup errors
        }
    }
}

/// <summary>
/// Exception thrown when a test should be skipped (e.g., Docker not available)
/// </summary>
public class SkipException : Exception
{
    public SkipException(string message) : base(message) { }
}
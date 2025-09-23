using FluentAssertions;
using System.Diagnostics;

namespace Forker.Resilience.Tests;

/// <summary>
/// Basic Docker validation tests to ensure Docker infrastructure works
/// before running full multi-process race condition validation.
/// </summary>
public class DockerValidationTests
{
    /// <summary>
    /// Basic test to validate Docker is working and we can run containers
    /// </summary>
    [Fact]
    public async Task Docker_BasicContainer_ShouldWork()
    {
        // Skip if Docker is not available
        if (!await IsDockerAvailable())
        {
            throw new SkipException("Docker is not available for testing");
        }

        var result = await RunDockerCommand("run --rm hello-world", TimeSpan.FromMinutes(2));
        result.Success.Should().BeTrue("Docker basic container should run successfully");
        result.Output.Should().Contain("Hello from Docker!", "Hello world container should produce expected output");
    }

    /// <summary>
    /// Test that we can run .NET containers
    /// </summary>
    [Fact]
    public async Task Docker_DotNetContainer_ShouldWork()
    {
        if (!await IsDockerAvailable())
        {
            throw new SkipException("Docker is not available for testing");
        }

        var result = await RunDockerCommand("run --rm mcr.microsoft.com/dotnet/sdk:8.0 dotnet --version", TimeSpan.FromMinutes(3));
        result.Success.Should().BeTrue("Docker .NET container should run successfully");
        result.Output.Should().Contain("8.0", ".NET container should report version 8.0");
    }

    /// <summary>
    /// Test Docker volume mounting for shared file system testing
    /// </summary>
    [Fact]
    public async Task Docker_VolumeMount_ShouldWork()
    {
        if (!await IsDockerAvailable())
        {
            throw new SkipException("Docker is not available for testing");
        }

        // Create a temporary directory for testing
        var tempDir = Path.Combine(Path.GetTempPath(), "docker-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempDir);

        try
        {
            // Create a test file
            var testFile = Path.Combine(tempDir, "test.txt");
            await File.WriteAllTextAsync(testFile, "Docker volume test");

            // Test volume mount and file access
            var mountPath = tempDir.Replace('\\', '/');
            var result = await RunDockerCommand($"run --rm -v \"{mountPath}:/test\" alpine cat /test/test.txt", TimeSpan.FromMinutes(2));

            result.Success.Should().BeTrue("Docker volume mount should work");
            result.Output.Should().Contain("Docker volume test", "Container should access mounted file");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    private async Task<bool> IsDockerAvailable()
    {
        try
        {
            var result = await RunDockerCommand("version", TimeSpan.FromSeconds(10));
            return result.Success;
        }
        catch
        {
            return false;
        }
    }

    private async Task<(bool Success, string Output, string Error)> RunDockerCommand(string arguments, TimeSpan timeout)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "docker",
            Arguments = arguments,
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

        var completed = await Task.Run(() => process.WaitForExit((int)timeout.TotalMilliseconds));

        if (!completed)
        {
            process.Kill();
            throw new TimeoutException($"Docker command timed out: docker {arguments}");
        }

        var output = outputBuilder.ToString();
        var error = errorBuilder.ToString();
        var success = process.ExitCode == 0;

        return (success, output, error);
    }
}


using Forker.Domain.Services;
using Forker.Infrastructure.Configuration;
using Forker.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Forker.Infrastructure.Tests.Integration;

/// <summary>
/// Integration tests for file discovery using real test data.
/// Tests the complete file discovery pipeline with actual medical imaging files.
/// </summary>
public sealed class FileDiscoveryIntegrationTests : IDisposable
{
    private readonly string _testSourceDirectory;
    private readonly string _realTestDataDirectory;
    private readonly FileDiscoveryService _discoveryService;
    private readonly FileStabilityChecker _stabilityChecker;
    private readonly List<FileDiscoveredEventArgs> _discoveredFiles;

    public FileDiscoveryIntegrationTests()
    {
        // Set up test directories
        _testSourceDirectory = Path.Combine(Path.GetTempPath(), "ForkerIntegrationTests", Guid.NewGuid().ToString());
        _realTestDataDirectory = Path.GetFullPath("tests/testData/source");
        Directory.CreateDirectory(_testSourceDirectory);

        // Configure for real medical imaging files
        var directories = new DirectoryConfiguration
        {
            Source = _testSourceDirectory
        };

        var monitoring = new FileMonitoringConfiguration
        {
            FileFilters = ["*.scn", "*.svs", "*.tiff", "*.ndpi"], // Medical imaging formats
            ExcludeExtensions = [".tmp", ".temp", ".lock", ".part"],
            MinimumFileAge = 2, // Seconds
            StabilityCheckInterval = 1, // Faster for tests
            MaxStabilityChecks = 5
        };

        // Use real implementations
        var stabilityLogger = new TestLogger<FileStabilityChecker>();
        _stabilityChecker = new FileStabilityChecker(Options.Create(monitoring), stabilityLogger);

        var discoveryLogger = new TestLogger<FileDiscoveryService>();
        _discoveryService = new FileDiscoveryService(
            Options.Create(directories),
            Options.Create(monitoring),
            _stabilityChecker,
            discoveryLogger);

        _discoveredFiles = new List<FileDiscoveredEventArgs>();
        _discoveryService.FileDiscovered += (_, args) => _discoveredFiles.Add(args);
    }

    [Fact]
    public async Task RealTestData_ScnFiles_AreDiscoveredCorrectly()
    {
        // This test verifies the discovery service works with the actual test data provided

        // Arrange - Check if real test data exists
        if (!Directory.Exists(_realTestDataDirectory))
        {
            // Skip test if test data is not available
            return;
        }

        var realFiles = Directory.GetFiles(_realTestDataDirectory, "*.scn");
        if (realFiles.Length == 0)
        {
            // Skip if no .scn files are available
            return;
        }

        // Copy real test files to our test directory
        foreach (var realFile in realFiles)
        {
            var fileName = Path.GetFileName(realFile);
            var targetPath = Path.Combine(_testSourceDirectory, fileName);
            File.Copy(realFile, targetPath);

            // Make files appear older to pass minimum age check
            File.SetCreationTimeUtc(targetPath, DateTime.UtcNow.AddMinutes(-5));
        }

        // Act
        await _discoveryService.StartAsync();
        var initialScan = await _discoveryService.ScanForExistingFilesAsync();

        // Wait a bit for any async discovery to complete
        await Task.Delay(3000);
        await _discoveryService.StopAsync();

        // Assert
        Assert.True(initialScan.Count >= realFiles.Length,
            $"Expected at least {realFiles.Length} files to be discovered, but got {initialScan.Count}");

        // Verify the files discovered are the ones we copied
        foreach (var realFile in realFiles)
        {
            var fileName = Path.GetFileName(realFile);
            Assert.Contains(initialScan, path => Path.GetFileName(path) == fileName);
        }

        // Verify file sizes are reasonable (medical imaging files should be large)
        foreach (var discoveredFile in _discoveredFiles.Where(f => f.FileSize > 0))
        {
            Assert.True(discoveredFile.FileSize > 1024 * 1024, // At least 1MB
                $"Medical imaging file {discoveredFile.FilePath} seems too small: {discoveredFile.FileSize} bytes");
        }
    }

    [Fact]
    public async Task LargeFileStability_MedicalImagingFiles_AreHandledCorrectly()
    {
        // This test verifies that large medical imaging files are properly checked for stability

        // Arrange - Check if real test data exists
        if (!Directory.Exists(_realTestDataDirectory))
        {
            return;
        }

        var realFiles = Directory.GetFiles(_realTestDataDirectory, "*.*");
        if (realFiles.Length == 0)
        {
            return;
        }

        // Use the largest file as our test subject
        var largestFile = realFiles.OrderByDescending(f => new FileInfo(f).Length).First();
        var testFilePath = Path.Combine(_testSourceDirectory, Path.GetFileName(largestFile));

        File.Copy(largestFile, testFilePath);
        File.SetCreationTimeUtc(testFilePath, DateTime.UtcNow.AddMinutes(-5));

        // Act
        var stabilityResult = await _stabilityChecker.WaitForStabilityAsync(testFilePath);

        // Assert
        Assert.True(stabilityResult.IsStable, $"Large file should be stable: {stabilityResult.UnstableReason}");
        Assert.True(stabilityResult.FileSize > 0, "File size should be greater than 0");

        var expectedSize = new FileInfo(largestFile).Length;
        Assert.Equal(expectedSize, stabilityResult.FileSize);
    }

    [Fact]
    public async Task FilePatternMatching_MedicalFormats_WorksCorrectly()
    {
        // This test verifies that medical imaging file formats are correctly identified

        // Arrange - Create test files with medical imaging extensions
        var medicalFormats = new[]
        {
            "test-scan.scn",     // Leica format (in our test data)
            "test-slide.svs",    // Aperio format
            "test-image.tiff",   // Standard TIFF
            "test-hamamatsu.ndpi", // Hamamatsu format
            "ignored.txt",       // Should be ignored
            "temp.scn.tmp"       // Should be ignored (temp file)
        };

        foreach (var fileName in medicalFormats)
        {
            var filePath = Path.Combine(_testSourceDirectory, fileName);
            await File.WriteAllTextAsync(filePath, "test content");
            File.SetCreationTimeUtc(filePath, DateTime.UtcNow.AddMinutes(-5));
        }

        // Act
        var discoveredFiles = await _discoveryService.ScanForExistingFilesAsync();

        // Assert
        Assert.Equal(4, discoveredFiles.Count); // Only the 4 medical formats, not txt or tmp

        var discoveredNames = discoveredFiles.Select(Path.GetFileName).ToArray();
        Assert.Contains("test-scan.scn", discoveredNames);
        Assert.Contains("test-slide.svs", discoveredNames);
        Assert.Contains("test-image.tiff", discoveredNames);
        Assert.Contains("test-hamamatsu.ndpi", discoveredNames);

        Assert.DoesNotContain("ignored.txt", discoveredNames);
        Assert.DoesNotContain("temp.scn.tmp", discoveredNames);
    }

    [Fact]
    public async Task EndToEndWorkflow_FileDiscoveryToEvent_CompletesSuccessfully()
    {
        // This test verifies the complete end-to-end workflow

        // Arrange
        await _discoveryService.StartAsync();

        // Act - Simulate a file being copied to the source directory
        var testFilePath = Path.Combine(_testSourceDirectory, "new-medical-file.scn");
        await File.WriteAllTextAsync(testFilePath, "Simulated medical imaging file content");

        // Wait for file system events and stability checking
        await Task.Delay(5000); // Give enough time for discovery and stability checks

        await _discoveryService.StopAsync();

        // Assert
        Assert.True(_discoveredFiles.Count > 0, "At least one file should have been discovered");

        var discoveredFile = _discoveredFiles.FirstOrDefault(f => Path.GetFileName(f.FilePath) == "new-medical-file.scn");
        Assert.NotNull(discoveredFile);
        Assert.True(discoveredFile.FileSize > 0);
        Assert.True(discoveredFile.DiscoveredAt <= DateTime.UtcNow);
    }

    public void Dispose()
    {
        _discoveryService?.Dispose();

        if (Directory.Exists(_testSourceDirectory))
        {
            try
            {
                Directory.Delete(Path.GetDirectoryName(_testSourceDirectory)!, true);
            }
            catch
            {
                // Ignore cleanup errors in tests
            }
        }
    }

    /// <summary>
    /// Test logger implementation that does nothing.
    /// </summary>
    private sealed class TestLogger<T> : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => false;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }
}
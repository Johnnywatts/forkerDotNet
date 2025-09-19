using Forker.Infrastructure.Configuration;
using Forker.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Forker.Infrastructure.Tests.Services;

/// <summary>
/// Unit tests for FileStabilityChecker service.
/// Tests file stability detection for large medical imaging files.
/// </summary>
public sealed class FileStabilityCheckerTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly FileStabilityChecker _stabilityChecker;

    public FileStabilityCheckerTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "ForkerTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testDirectory);

        var config = new FileMonitoringConfiguration
        {
            MinimumFileAge = 1, // 1 second for faster tests
            StabilityCheckInterval = 1, // 1 second for faster tests
            MaxStabilityChecks = 3
        };

        var logger = new TestLogger<FileStabilityChecker>();
        _stabilityChecker = new FileStabilityChecker(Options.Create(config), logger);
    }

    [Fact]
    public async Task IsFileStableAsync_NonExistentFile_ReturnsFalse()
    {
        // Arrange
        var nonExistentFile = Path.Combine(_testDirectory, "nonexistent.svs");

        // Act
        var result = await _stabilityChecker.IsFileStableAsync(nonExistentFile);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task IsFileStableAsync_NewFile_ReturnsFalse()
    {
        // Arrange
        var testFile = Path.Combine(_testDirectory, "new-file.svs");
        await File.WriteAllTextAsync(testFile, "test content");

        // Act
        var result = await _stabilityChecker.IsFileStableAsync(testFile);

        // Assert
        Assert.False(result); // File is too new (less than minimum age)
    }

    [Fact]
    public async Task IsFileStableAsync_OldStableFile_ReturnsTrue()
    {
        // Arrange
        var testFile = Path.Combine(_testDirectory, "old-file.svs");
        await File.WriteAllTextAsync(testFile, "test content");

        // Make file appear older by setting creation time
        File.SetCreationTimeUtc(testFile, DateTime.UtcNow.AddMinutes(-5));

        // Act
        var result = await _stabilityChecker.IsFileStableAsync(testFile);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task WaitForStabilityAsync_StableFile_ReturnsStableResult()
    {
        // Arrange
        var testFile = Path.Combine(_testDirectory, "stable-file.svs");
        const string content = "stable test content";
        await File.WriteAllTextAsync(testFile, content);

        // Make file appear older
        File.SetCreationTimeUtc(testFile, DateTime.UtcNow.AddMinutes(-5));

        // Act
        var result = await _stabilityChecker.WaitForStabilityAsync(testFile);

        // Assert
        Assert.True(result.IsStable);
        Assert.Equal(content.Length, result.FileSize);
        Assert.True(result.ChecksPerformed >= 1);
        Assert.Null(result.UnstableReason);
    }

    [Fact]
    public async Task WaitForStabilityAsync_GrowingFile_ReturnsUnstableResult()
    {
        // Arrange
        var testFile = Path.Combine(_testDirectory, "growing-file.svs");
        await File.WriteAllTextAsync(testFile, "initial content");

        // Make file appear older
        File.SetCreationTimeUtc(testFile, DateTime.UtcNow.AddMinutes(-5));

        // Start background task to modify file during stability check
        var modificationTask = Task.Run(async () =>
        {
            await Task.Delay(500); // Wait a bit before modifying
            await File.AppendAllTextAsync(testFile, " additional content");
        });

        // Act
        var result = await _stabilityChecker.WaitForStabilityAsync(testFile);

        // Assert
        await modificationTask; // Ensure modification completed
        Assert.False(result.IsStable);
        Assert.Contains("did not stabilize", result.UnstableReason);
    }

    [Fact]
    public async Task WaitForStabilityAsync_DisappearedFile_ReturnsUnstableResult()
    {
        // Arrange
        var testFile = Path.Combine(_testDirectory, "disappearing-file.svs");
        await File.WriteAllTextAsync(testFile, "test content");

        // Start background task to delete file during stability check
        var deletionTask = Task.Run(async () =>
        {
            await Task.Delay(500); // Wait a bit before deleting
            File.Delete(testFile);
        });

        // Act
        var result = await _stabilityChecker.WaitForStabilityAsync(testFile);

        // Assert
        await deletionTask; // Ensure deletion completed
        Assert.False(result.IsStable);
        Assert.Equal("File no longer exists", result.UnstableReason);
    }

    [Fact]
    public async Task WaitForStabilityAsync_WithCancellation_ThrowsOperationCancelledException()
    {
        // Arrange
        var testFile = Path.Combine(_testDirectory, "test-file.svs");
        await File.WriteAllTextAsync(testFile, "test content");

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _stabilityChecker.WaitForStabilityAsync(testFile, cts.Token));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task IsFileStableAsync_InvalidFilePath_ThrowsArgumentException(string invalidPath)
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => _stabilityChecker.IsFileStableAsync(invalidPath));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task WaitForStabilityAsync_InvalidFilePath_ThrowsArgumentException(string invalidPath)
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => _stabilityChecker.WaitForStabilityAsync(invalidPath));
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            try
            {
                Directory.Delete(_testDirectory, true);
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
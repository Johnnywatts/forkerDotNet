using System.Security.Cryptography;
using System.Text;
using Forker.Infrastructure.Services;
using Microsoft.Extensions.Logging;

namespace Forker.Infrastructure.Tests.Services;

/// <summary>
/// Unit tests for HashingService.
/// Tests SHA-256 hash calculation for medical imaging files with streaming operations.
/// </summary>
public sealed class HashingServiceTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly HashingService _hashingService;

    public HashingServiceTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "ForkerTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testDirectory);

        var logger = new TestLogger<HashingService>();
        _hashingService = new HashingService(logger);
    }

    [Fact]
    public async Task CalculateHashAsync_WithValidFile_ReturnsCorrectSHA256Hash()
    {
        // Arrange
        const string testContent = "This is test content for SHA-256 hashing";
        var testFile = Path.Combine(_testDirectory, "test-file.txt");
        await File.WriteAllTextAsync(testFile, testContent);

        // Calculate expected hash manually
        var expectedHash = CalculateExpectedHash(testContent);

        // Act
        var actualHash = await _hashingService.CalculateHashAsync(testFile);

        // Assert
        Assert.Equal(expectedHash, actualHash);
    }

    [Fact]
    public async Task CalculateHashAsync_WithLargeFile_ReturnsCorrectHash()
    {
        // Arrange - Create a 1MB test file
        var testFile = Path.Combine(_testDirectory, "large-test-file.dat");
        var testData = new byte[1024 * 1024]; // 1MB
        new Random(42).NextBytes(testData); // Use seed for reproducible results

        await File.WriteAllBytesAsync(testFile, testData);

        // Calculate expected hash
        var expectedHash = CalculateExpectedHash(testData);

        // Act
        var actualHash = await _hashingService.CalculateHashAsync(testFile);

        // Assert
        Assert.Equal(expectedHash, actualHash);
    }

    [Fact]
    public async Task CalculateHashAsync_WithStream_ReturnsCorrectHash()
    {
        // Arrange
        const string testContent = "Stream hash test content";
        var testData = Encoding.UTF8.GetBytes(testContent);
        var expectedHash = CalculateExpectedHash(testData);

        using var stream = new MemoryStream(testData);

        // Act
        var actualHash = await _hashingService.CalculateHashAsync(stream);

        // Assert
        Assert.Equal(expectedHash, actualHash);
    }

    [Fact]
    public async Task CalculateHashAsync_WithMedicalImagingFile_ReturnsCorrectHash()
    {
        // Arrange - Check if real test data exists
        var realTestDataDirectory = Path.GetFullPath("tests/testData/source");
        if (!Directory.Exists(realTestDataDirectory))
        {
            // Skip test if test data is not available
            return;
        }

        var realFiles = Directory.GetFiles(realTestDataDirectory, "*.scn");
        if (realFiles.Length == 0)
        {
            // Skip if no .scn files are available
            return;
        }

        var testFile = realFiles[0]; // Use first available medical imaging file

        // Act
        var hash1 = await _hashingService.CalculateHashAsync(testFile);
        var hash2 = await _hashingService.CalculateHashAsync(testFile);

        // Assert
        Assert.NotEmpty(hash1);
        Assert.Equal(64, hash1.Length); // SHA-256 is 32 bytes = 64 hex chars
        Assert.Equal(hash1, hash2); // Same file should produce same hash
        Assert.All(hash1, c => Assert.True(char.IsAsciiHexDigitLower(c))); // Should be lowercase hex
    }

    [Fact]
    public async Task CalculateHashAsync_WithNonExistentFile_ThrowsFileNotFoundException()
    {
        // Arrange
        var nonExistentFile = Path.Combine(_testDirectory, "non-existent.txt");

        // Act & Assert
        await Assert.ThrowsAsync<FileNotFoundException>(
            () => _hashingService.CalculateHashAsync(nonExistentFile));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task CalculateHashAsync_WithInvalidFilePath_ThrowsArgumentException(string invalidPath)
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => _hashingService.CalculateHashAsync(invalidPath));
    }

    [Fact]
    public async Task CalculateHashAsync_WithNullStream_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _hashingService.CalculateHashAsync((Stream)null!));
    }

    [Fact]
    public async Task CalculateHashAsync_WithNonReadableStream_ThrowsArgumentException()
    {
        // Arrange
        var tempFile = Path.Combine(_testDirectory, "temp.txt");
        await File.WriteAllTextAsync(tempFile, "test");

        using var stream = new FileStream(tempFile, FileMode.Open, FileAccess.Write);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => _hashingService.CalculateHashAsync(stream));
    }

    [Fact]
    public async Task CalculateHashAsync_WithEmptyFile_ReturnsEmptyContentHash()
    {
        // Arrange
        var emptyFile = Path.Combine(_testDirectory, "empty-file.txt");
        await File.WriteAllTextAsync(emptyFile, string.Empty);

        var expectedHash = CalculateExpectedHash(string.Empty);

        // Act
        var actualHash = await _hashingService.CalculateHashAsync(emptyFile);

        // Assert
        Assert.Equal(expectedHash, actualHash);
    }

    [Fact]
    public async Task CalculateHashAsync_WithCancellation_ThrowsOperationCancelledException()
    {
        // Arrange
        var testFile = Path.Combine(_testDirectory, "test-file.txt");
        await File.WriteAllTextAsync(testFile, "test content");

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel immediately

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _hashingService.CalculateHashAsync(testFile, cts.Token));
    }

    private static string CalculateExpectedHash(string content)
    {
        return CalculateExpectedHash(Encoding.UTF8.GetBytes(content));
    }

    private static string CalculateExpectedHash(byte[] data)
    {
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(data);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
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
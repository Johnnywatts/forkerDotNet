using Forker.Domain.Services;

namespace Forker.Domain.Tests;

/// <summary>
/// Unit tests for VerificationResult value object.
/// Tests creation, validation, and hash matching logic.
/// </summary>
public class VerificationResultTests
{
    [Fact]
    public void SuccessfulVerification_WithMatchingHash_ShouldBeValid()
    {
        // Arrange
        var filePath = "/test/file.medical";
        var hash = "abcd1234567890abcd1234567890abcd1234567890abcd1234567890abcd1234";
        var fileSize = 1024L;
        var duration = TimeSpan.FromSeconds(5);

        // Act
        var result = new VerificationResult(filePath, hash, hash, fileSize, duration);

        // Assert
        Assert.Equal(filePath, result.FilePath);
        Assert.Equal(hash, result.ComputedHash);
        Assert.Equal(hash, result.ExpectedHash);
        Assert.True(result.IsMatch);
        Assert.Equal(fileSize, result.FileSize);
        Assert.Equal(duration, result.VerificationDuration);
        Assert.True(result.VerificationSucceeded);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void SuccessfulVerification_WithNonMatchingHash_ShouldIndicateMismatch()
    {
        // Arrange
        var filePath = "/test/file.medical";
        var computedHash = "abcd1234567890abcd1234567890abcd1234567890abcd1234567890abcd1234";
        var expectedHash = "efgh5678901234efgh5678901234efgh5678901234efgh5678901234efgh5678";
        var fileSize = 1024L;
        var duration = TimeSpan.FromSeconds(5);

        // Act
        var result = new VerificationResult(filePath, computedHash, expectedHash, fileSize, duration);

        // Assert
        Assert.Equal(filePath, result.FilePath);
        Assert.Equal(computedHash, result.ComputedHash);
        Assert.Equal(expectedHash, result.ExpectedHash);
        Assert.False(result.IsMatch);
        Assert.Equal(fileSize, result.FileSize);
        Assert.Equal(duration, result.VerificationDuration);
        Assert.True(result.VerificationSucceeded);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void FailedVerification_DueToIOError_ShouldIndicateFailure()
    {
        // Arrange
        var filePath = "/test/file.medical";
        var expectedHash = "abcd1234567890abcd1234567890abcd1234567890abcd1234567890abcd1234";
        var errorMessage = "File not found during verification";

        // Act
        var result = new VerificationResult(filePath, expectedHash, errorMessage);

        // Assert
        Assert.Equal(filePath, result.FilePath);
        Assert.Equal(expectedHash, result.ExpectedHash);
        Assert.Equal(errorMessage, result.ErrorMessage);
        Assert.Equal(string.Empty, result.ComputedHash);
        Assert.False(result.IsMatch);
        Assert.Equal(0, result.FileSize);
        Assert.Equal(TimeSpan.Zero, result.VerificationDuration);
        Assert.False(result.VerificationSucceeded);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_WithInvalidFilePath_ShouldThrowArgumentException(string invalidPath)
    {
        // Arrange
        var hash = "abcd1234567890abcd1234567890abcd1234567890abcd1234567890abcd1234";
        var fileSize = 1024L;
        var duration = TimeSpan.FromSeconds(5);

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            new VerificationResult(invalidPath, hash, hash, fileSize, duration));
        Assert.Contains("File path cannot be null, empty, or whitespace", exception.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_WithInvalidComputedHash_ShouldThrowArgumentException(string invalidHash)
    {
        // Arrange
        var filePath = "/test/file.medical";
        var expectedHash = "abcd1234567890abcd1234567890abcd1234567890abcd1234567890abcd1234";
        var fileSize = 1024L;
        var duration = TimeSpan.FromSeconds(5);

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            new VerificationResult(filePath, invalidHash, expectedHash, fileSize, duration));
        Assert.Contains("Hash cannot be null, empty, or whitespace", exception.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_WithInvalidExpectedHash_ShouldThrowArgumentException(string invalidHash)
    {
        // Arrange
        var filePath = "/test/file.medical";
        var computedHash = "abcd1234567890abcd1234567890abcd1234567890abcd1234567890abcd1234";
        var fileSize = 1024L;
        var duration = TimeSpan.FromSeconds(5);

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            new VerificationResult(filePath, computedHash, invalidHash, fileSize, duration));
        Assert.Contains("Hash cannot be null, empty, or whitespace", exception.Message);
    }

    [Fact]
    public void Constructor_WithNegativeFileSize_ShouldThrowArgumentOutOfRangeException()
    {
        // Arrange
        var filePath = "/test/file.medical";
        var hash = "abcd1234567890abcd1234567890abcd1234567890abcd1234567890abcd1234";
        var negativeFileSize = -1L;
        var duration = TimeSpan.FromSeconds(5);

        // Act & Assert
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new VerificationResult(filePath, hash, hash, negativeFileSize, duration));
        Assert.Contains("File size cannot be negative", exception.Message);
    }

    [Fact]
    public void HashComparison_ShouldBeCaseInsensitive()
    {
        // Arrange
        var filePath = "/test/file.medical";
        var computedHashLowercase = "abcd1234567890abcd1234567890abcd1234567890abcd1234567890abcd1234";
        var expectedHashUppercase = "ABCD1234567890ABCD1234567890ABCD1234567890ABCD1234567890ABCD1234";
        var fileSize = 1024L;
        var duration = TimeSpan.FromSeconds(5);

        // Act
        var result = new VerificationResult(filePath, computedHashLowercase, expectedHashUppercase, fileSize, duration);

        // Assert
        Assert.True(result.IsMatch);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FailedConstructor_WithInvalidErrorMessage_ShouldThrowArgumentException(string invalidErrorMessage)
    {
        // Arrange
        var filePath = "/test/file.medical";
        var expectedHash = "abcd1234567890abcd1234567890abcd1234567890abcd1234567890abcd1234";

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            new VerificationResult(filePath, expectedHash, invalidErrorMessage));
        Assert.Contains("Error message cannot be null, empty, or whitespace", exception.Message);
    }

    [Fact]
    public void Constructor_ShouldTrimWhitespaceFromInputs()
    {
        // Arrange
        var filePath = "  /test/file.medical  ";
        var computedHash = "  abcd1234567890abcd1234567890abcd1234567890abcd1234567890abcd1234  ";
        var expectedHash = "  abcd1234567890abcd1234567890abcd1234567890abcd1234567890abcd1234  ";
        var fileSize = 1024L;
        var duration = TimeSpan.FromSeconds(5);

        // Act
        var result = new VerificationResult(filePath, computedHash, expectedHash, fileSize, duration);

        // Assert
        Assert.Equal("/test/file.medical", result.FilePath);
        Assert.Equal("abcd1234567890abcd1234567890abcd1234567890abcd1234567890abcd1234", result.ComputedHash);
        Assert.Equal("abcd1234567890abcd1234567890abcd1234567890abcd1234567890abcd1234", result.ExpectedHash);
        Assert.True(result.IsMatch);
    }
}
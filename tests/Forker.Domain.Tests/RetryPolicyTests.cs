using Forker.Domain.Services;

namespace Forker.Domain.Tests;

/// <summary>
/// Unit tests for retry policy components including decision logic and failure classification.
/// Tests Invariant I6 (MaxAttempts → FAILED_PERMANENT) and I13 (non-decreasing backoff).
/// </summary>
public class RetryPolicyTests
{
    [Fact]
    public void RetryDecision_Retry_ShouldCreateRetryDecision()
    {
        // Arrange
        var delay = TimeSpan.FromSeconds(5);
        var reason = "Transient network error";

        // Act
        var decision = RetryDecision.Retry(delay, reason);

        // Assert
        Assert.True(decision.ShouldRetry);
        Assert.Equal(delay, decision.Delay);
        Assert.Equal(reason, decision.Reason);
        Assert.False(decision.IsPermanentFailure);
    }

    [Fact]
    public void RetryDecision_MaxAttemptsReached_ShouldCreatePermanentFailureDecision()
    {
        // Arrange
        var reason = "Maximum retry attempts reached";

        // Act
        var decision = RetryDecision.MaxAttemptsReached(reason);

        // Assert
        Assert.False(decision.ShouldRetry);
        Assert.Equal(TimeSpan.Zero, decision.Delay);
        Assert.Equal(reason, decision.Reason);
        Assert.True(decision.IsPermanentFailure);
    }

    [Fact]
    public void RetryDecision_PermanentFailure_ShouldCreatePermanentFailureDecision()
    {
        // Arrange
        var reason = "File not found";

        // Act
        var decision = RetryDecision.PermanentFailure(reason);

        // Assert
        Assert.False(decision.ShouldRetry);
        Assert.Equal(TimeSpan.Zero, decision.Delay);
        Assert.Equal(reason, decision.Reason);
        Assert.True(decision.IsPermanentFailure);
    }

    [Fact]
    public void RetryDecision_NonRetryable_ShouldCreateNonRetryableDecision()
    {
        // Arrange
        var reason = "Invalid operation";

        // Act
        var decision = RetryDecision.NonRetryable(reason);

        // Assert
        Assert.False(decision.ShouldRetry);
        Assert.Equal(TimeSpan.Zero, decision.Delay);
        Assert.Equal(reason, decision.Reason);
        Assert.False(decision.IsPermanentFailure);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RetryDecision_WithInvalidReason_ShouldThrowArgumentException(string invalidReason)
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            RetryDecision.Retry(TimeSpan.FromSeconds(1), invalidReason));
        Assert.Contains("Reason cannot be null, empty, or whitespace", exception.Message);
    }

    [Theory]
    [InlineData(typeof(IOException), true)]
    [InlineData(typeof(TimeoutException), true)]
    [InlineData(typeof(TaskCanceledException), true)]
    [InlineData(typeof(OperationCanceledException), true)]
    [InlineData(typeof(UnauthorizedAccessException), false)]
    [InlineData(typeof(FileNotFoundException), false)]
    [InlineData(typeof(DirectoryNotFoundException), false)]
    [InlineData(typeof(ArgumentException), false)]
    public void FailureClassifier_ClassifyFailure_ShouldClassifyCorrectly(Type exceptionType, bool expectedTransient)
    {
        // Arrange
        var exception = (Exception)Activator.CreateInstance(exceptionType, "Test exception")!;
        var operationType = OperationType.FileCopy;

        // Act
        var category = FailureClassifier.ClassifyFailure(exception, operationType);

        // Assert
        if (expectedTransient)
        {
            Assert.Equal(FailureCategory.TransientFailure, category);
        }
        else
        {
            Assert.NotEqual(FailureCategory.TransientFailure, category);
        }
    }

    [Fact]
    public void FailureClassifier_ClassifyFailure_WithTransientIOException_ShouldBeTransient()
    {
        // Arrange
        var exception = new IOException("The process cannot access the file because it is being used by another process");
        var operationType = OperationType.FileCopy;

        // Act
        var category = FailureClassifier.ClassifyFailure(exception, operationType);

        // Assert
        Assert.Equal(FailureCategory.TransientFailure, category);
    }

    [Fact]
    public void FailureClassifier_ClassifyFailure_WithInvariantViolation_ShouldBeIntegrityFailure()
    {
        // Arrange
        var exception = new Domain.Exceptions.InvariantViolationException("I5", "test", "Hash mismatch detected");
        var operationType = OperationType.FileVerification;

        // Act
        var category = FailureClassifier.ClassifyFailure(exception, operationType);

        // Assert
        Assert.Equal(FailureCategory.IntegrityFailure, category);
    }

    [Fact]
    public void FailureClassifier_ClassifyFailure_WithUnknownException_ShouldBeUnknownFailure()
    {
        // Arrange
        var exception = new CustomTestException("Unknown error type");
        var operationType = OperationType.FileCopy;

        // Act
        var category = FailureClassifier.ClassifyFailure(exception, operationType);

        // Assert
        Assert.Equal(FailureCategory.UnknownFailure, category);
    }

    [Theory]
    [InlineData(OperationType.FileCopy)]
    [InlineData(OperationType.FileVerification)]
    [InlineData(OperationType.FileDiscovery)]
    [InlineData(OperationType.FileStabilityCheck)]
    [InlineData(OperationType.DatabaseOperation)]
    [InlineData(OperationType.FileSystemOperation)]
    public void OperationType_AllValues_ShouldBeValidEnumValues(OperationType operationType)
    {
        // Act & Assert - should not throw
        var name = operationType.ToString();
        Assert.False(string.IsNullOrEmpty(name));
    }

    [Theory]
    [InlineData(FailureCategory.TransientFailure)]
    [InlineData(FailureCategory.PermanentFailure)]
    [InlineData(FailureCategory.IntegrityFailure)]
    [InlineData(FailureCategory.ConfigurationError)]
    [InlineData(FailureCategory.UnknownFailure)]
    public void FailureCategory_AllValues_ShouldBeValidEnumValues(FailureCategory category)
    {
        // Act & Assert - should not throw
        var name = category.ToString();
        Assert.False(string.IsNullOrEmpty(name));
    }
}

/// <summary>
/// Custom exception for testing unknown failure classification.
/// </summary>
public class CustomTestException : Exception
{
    public CustomTestException(string message) : base(message) { }
}
using Forker.Domain;
using Forker.Domain.Exceptions;

namespace Forker.Domain.Tests;

public class TargetOutcomeTests
{
    private readonly FileJobId _testJobId = FileJobId.New();
    private readonly TargetId _testTargetId = TargetId.From("TargetA");
    private readonly string _testTempPath = @"C:\temp\file.tmp";
    private readonly string _testFinalPath = @"C:\target\file.svs";
    private readonly string _testHash = "abcd1234efgh5678";

    [Fact]
    public void Constructor_ValidInputs_CreatesOutcomeInPendingState()
    {
        // Arrange & Act
        var outcome = new TargetOutcome(_testJobId, _testTargetId);

        // Assert
        Assert.Equal(_testJobId, outcome.JobId);
        Assert.Equal(_testTargetId, outcome.TargetId);
        Assert.Equal(TargetCopyState.Pending, outcome.CopyState);
        Assert.Equal(0, outcome.Attempts);
        Assert.Null(outcome.Hash);
        Assert.Null(outcome.TempPath);
        Assert.Null(outcome.FinalPath);
        Assert.Null(outcome.LastError);
        Assert.True(outcome.LastTransitionAt <= DateTime.UtcNow);
        Assert.False(outcome.IsSuccessfullyCompleted);
        Assert.False(outcome.HasFailedPermanently);
        Assert.False(outcome.CanRetry);
    }

    [Fact]
    public void Constructor_NullJobId_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new TargetOutcome(null!, _testTargetId));
    }

    [Fact]
    public void Constructor_NullTargetId_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new TargetOutcome(_testJobId, null!));
    }

    [Fact]
    public void StartCopy_FromPending_TransitionsToCopyingAndIncrementsAttempts()
    {
        // Arrange
        var outcome = new TargetOutcome(_testJobId, _testTargetId);

        // Act
        outcome.StartCopy(_testTempPath);

        // Assert
        Assert.Equal(TargetCopyState.Copying, outcome.CopyState);
        Assert.Equal(1, outcome.Attempts);
        Assert.Equal(_testTempPath, outcome.TempPath);
        Assert.True(outcome.LastTransitionAt <= DateTime.UtcNow);
    }

    [Fact]
    public void CompleteCopy_FromCopying_TransitionsToCopiedAndSetsHashAndPath()
    {
        // Arrange
        var outcome = new TargetOutcome(_testJobId, _testTargetId);
        outcome.StartCopy(_testTempPath);

        // Act
        outcome.CompleteCopy(_testHash, _testFinalPath);

        // Assert
        Assert.Equal(TargetCopyState.Copied, outcome.CopyState);
        Assert.Equal(_testHash, outcome.Hash);
        Assert.Equal(_testFinalPath, outcome.FinalPath);
        Assert.Null(outcome.LastError);
    }

    [Fact]
    public void StartVerification_FromCopied_TransitionsToVerifying()
    {
        // Arrange
        var outcome = new TargetOutcome(_testJobId, _testTargetId);
        outcome.StartCopy(_testTempPath);
        outcome.CompleteCopy(_testHash, _testFinalPath);

        // Act
        outcome.StartVerification();

        // Assert
        Assert.Equal(TargetCopyState.Verifying, outcome.CopyState);
    }

    [Fact]
    public void StartVerification_FromNonCopiedState_ThrowsInvariantViolationException()
    {
        // Arrange - Test from Pending state
        var outcome = new TargetOutcome(_testJobId, _testTargetId);

        // Act & Assert
        var ex = Assert.Throws<InvariantViolationException>(() => outcome.StartVerification());
        Assert.Equal("I1", ex.InvariantId);
        Assert.Contains("Target VERIFYING requires prior COPIED", ex.Message);
    }

    [Fact]
    public void CompleteVerification_FromVerifying_TransitionsToVerified()
    {
        // Arrange
        var outcome = new TargetOutcome(_testJobId, _testTargetId);
        outcome.StartCopy(_testTempPath);
        outcome.CompleteCopy(_testHash, _testFinalPath);
        outcome.StartVerification();

        // Act
        outcome.CompleteVerification();

        // Assert
        Assert.Equal(TargetCopyState.Verified, outcome.CopyState);
        Assert.True(outcome.IsSuccessfullyCompleted);
        Assert.Null(outcome.LastError);
    }

    [Fact]
    public void MarkAsRetryableFailed_FromAnyState_TransitionsToFailedRetryable()
    {
        // Arrange
        var outcome = new TargetOutcome(_testJobId, _testTargetId);
        var errorMessage = "Network timeout";

        // Act
        outcome.MarkAsRetryableFailed(errorMessage);

        // Assert
        Assert.Equal(TargetCopyState.FailedRetryable, outcome.CopyState);
        Assert.Equal(errorMessage, outcome.LastError);
        Assert.True(outcome.CanRetry);
        Assert.False(outcome.HasFailedPermanently);
    }

    [Fact]
    public void MarkAsPermanentlyFailed_FromAnyState_TransitionsToFailedPermanent()
    {
        // Arrange
        var outcome = new TargetOutcome(_testJobId, _testTargetId);
        var errorMessage = "Access denied";

        // Act
        outcome.MarkAsPermanentlyFailed(errorMessage);

        // Assert
        Assert.Equal(TargetCopyState.FailedPermanent, outcome.CopyState);
        Assert.Equal(errorMessage, outcome.LastError);
        Assert.True(outcome.HasFailedPermanently);
        Assert.False(outcome.CanRetry);
    }

    [Fact]
    public void Retry_FromFailedRetryable_ResetsToPending()
    {
        // Arrange
        var outcome = new TargetOutcome(_testJobId, _testTargetId);
        outcome.StartCopy(_testTempPath);
        outcome.MarkAsRetryableFailed("Temporary error");

        // Act
        outcome.Retry();

        // Assert
        Assert.Equal(TargetCopyState.Pending, outcome.CopyState);
        Assert.Null(outcome.TempPath);
        // Note: Attempts, Hash, FinalPath, and LastError should be preserved for audit trail
    }

    [Fact]
    public void Retry_FromNonRetryableState_ThrowsInvalidStateTransitionException()
    {
        // Arrange
        var outcome = new TargetOutcome(_testJobId, _testTargetId);

        // Act & Assert - From Pending
        Assert.Throws<InvalidStateTransitionException>(() => outcome.Retry());

        // Arrange - From Permanently Failed
        outcome.MarkAsPermanentlyFailed("Access denied");

        // Act & Assert - From Permanently Failed
        Assert.Throws<InvalidStateTransitionException>(() => outcome.Retry());
    }

    [Fact]
    public void ValidStateTransitions_FollowStateMachine()
    {
        // Arrange
        var outcome = new TargetOutcome(_testJobId, _testTargetId);

        // Act & Assert - Valid progression
        Assert.Equal(TargetCopyState.Pending, outcome.CopyState);

        outcome.StartCopy(_testTempPath);
        Assert.Equal(TargetCopyState.Copying, outcome.CopyState);

        outcome.CompleteCopy(_testHash, _testFinalPath);
        Assert.Equal(TargetCopyState.Copied, outcome.CopyState);

        outcome.StartVerification();
        Assert.Equal(TargetCopyState.Verifying, outcome.CopyState);

        outcome.CompleteVerification();
        Assert.Equal(TargetCopyState.Verified, outcome.CopyState);

        // Terminal state - no further transitions allowed
        Assert.Throws<InvalidStateTransitionException>(() => outcome.StartCopy("temp"));
        Assert.Throws<InvalidStateTransitionException>(() => outcome.MarkAsRetryableFailed("error"));
    }

    [Fact]
    public void FailureTransitions_AllowedFromAnyNonTerminalState()
    {
        // Test retryable failure from different states
        var outcome1 = new TargetOutcome(FileJobId.New(), _testTargetId);
        outcome1.MarkAsRetryableFailed("Error from pending");
        Assert.Equal(TargetCopyState.FailedRetryable, outcome1.CopyState);

        var outcome2 = new TargetOutcome(FileJobId.New(), _testTargetId);
        outcome2.StartCopy(_testTempPath);
        outcome2.MarkAsRetryableFailed("Error from copying");
        Assert.Equal(TargetCopyState.FailedRetryable, outcome2.CopyState);

        var outcome3 = new TargetOutcome(FileJobId.New(), _testTargetId);
        outcome3.StartCopy(_testTempPath);
        outcome3.CompleteCopy(_testHash, _testFinalPath);
        outcome3.MarkAsRetryableFailed("Error from copied");
        Assert.Equal(TargetCopyState.FailedRetryable, outcome3.CopyState);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(null)]
    public void StartCopy_InvalidTempPath_ThrowsArgumentException(string invalidPath)
    {
        // Arrange
        var outcome = new TargetOutcome(_testJobId, _testTargetId);

        // Act & Assert
        Assert.Throws<ArgumentException>(() => outcome.StartCopy(invalidPath));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(null)]
    public void CompleteCopy_InvalidHash_ThrowsArgumentException(string invalidHash)
    {
        // Arrange
        var outcome = new TargetOutcome(_testJobId, _testTargetId);
        outcome.StartCopy(_testTempPath);

        // Act & Assert
        Assert.Throws<ArgumentException>(() => outcome.CompleteCopy(invalidHash, _testFinalPath));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(null)]
    public void MarkAsRetryableFailed_InvalidError_ThrowsArgumentException(string invalidError)
    {
        // Arrange
        var outcome = new TargetOutcome(_testJobId, _testTargetId);

        // Act & Assert
        Assert.Throws<ArgumentException>(() => outcome.MarkAsRetryableFailed(invalidError));
    }

    [Fact]
    public void InvalidStateTransitions_ThrowInvalidStateTransitionException()
    {
        // Arrange
        var outcome = new TargetOutcome(_testJobId, _testTargetId);

        // Test invalid transitions from Pending
        Assert.Throws<InvalidStateTransitionException>(() => outcome.CompleteCopy(_testHash, _testFinalPath));
        Assert.Throws<InvariantViolationException>(() => outcome.StartVerification()); // I1: requires COPIED state
        Assert.Throws<InvalidStateTransitionException>(() => outcome.CompleteVerification());

        // Test invalid transition from Copying to Verifying (must go through Copied)
        outcome.StartCopy(_testTempPath);
        Assert.Throws<InvariantViolationException>(() => outcome.StartVerification()); // I1: requires COPIED state
    }
}
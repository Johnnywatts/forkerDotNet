
using Forker.Domain;
using Forker.Domain.Exceptions;

namespace Forker.Domain.Tests;

public class FileJobTests
{
    private readonly FileJobId _testJobId = FileJobId.New();
    private readonly TargetId _targetA = TargetId.From("TargetA");
    private readonly TargetId _targetB = TargetId.From("TargetB");
    private readonly string _testSourcePath = @"C:\test\file.svs";
    private readonly long _testFileSize = 1024L;

    [Fact]
    public void Constructor_ValidInputs_CreatesJobInDiscoveredState()
    {
        // Arrange & Act
        var job = new FileJob(_testJobId, _testSourcePath, _testFileSize, [_targetA, _targetB]);

        // Assert
        Assert.Equal(_testJobId, job.Id);
        Assert.Equal(_testSourcePath, job.SourcePath);
        Assert.Equal(_testFileSize, job.InitialSize);
        Assert.Equal(JobState.Discovered, job.State);
        Assert.Equal(2, job.RequiredTargets.Count);
        Assert.Contains(_targetA, job.RequiredTargets);
        Assert.Contains(_targetB, job.RequiredTargets);
        Assert.Null(job.SourceHash);
        Assert.Equal(VersionToken.Initial, job.VersionToken);
        Assert.True(job.CreatedAt <= DateTime.UtcNow);
    }

    [Fact]
    public void Constructor_NullJobId_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new FileJob(null!, _testSourcePath, _testFileSize, [_targetA]));
    }

    [Fact]
    public void Constructor_EmptyTargets_ThrowsArgumentException()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            new FileJob(_testJobId, _testSourcePath, _testFileSize, []));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(null)]
    public void Constructor_InvalidSourcePath_ThrowsArgumentException(string invalidPath)
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            new FileJob(_testJobId, invalidPath, _testFileSize, [_targetA]));
    }

    [Fact]
    public void Constructor_NegativeFileSize_ThrowsArgumentOutOfRangeException()
    {
        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new FileJob(_testJobId, _testSourcePath, -1L, [_targetA]));
    }

    [Fact]
    public void SetSourceHash_FirstTime_SetsHashAndIncrementsVersion()
    {
        // Arrange
        var job = new FileJob(_testJobId, _testSourcePath, _testFileSize, [_targetA]);
        var hash = "abcd1234";
        var initialVersion = job.VersionToken;

        // Act
        job.SetSourceHash(hash);

        // Assert
        Assert.Equal(hash, job.SourceHash);
        Assert.Equal(initialVersion.Next(), job.VersionToken);
    }

    [Fact]
    public void SetSourceHash_SecondTime_ThrowsInvariantViolationException()
    {
        // Arrange
        var job = new FileJob(_testJobId, _testSourcePath, _testFileSize, [_targetA]);
        job.SetSourceHash("hash1");

        // Act & Assert
        var ex = Assert.Throws<InvariantViolationException>(() => job.SetSourceHash("hash2"));
        Assert.Equal("I10", ex.InvariantId);
        Assert.Contains("SourceHash is immutable", ex.Message);
    }

    [Fact]
    public void MarkAsQueued_FromDiscovered_TransitionsSuccessfully()
    {
        // Arrange
        var job = new FileJob(_testJobId, _testSourcePath, _testFileSize, [_targetA]);
        var initialVersion = job.VersionToken;

        // Act
        job.MarkAsQueued();

        // Assert
        Assert.Equal(JobState.Queued, job.State);
        Assert.Equal(initialVersion.Next(), job.VersionToken);
    }

    [Fact]
    public void MarkAsQueued_FromInvalidState_ThrowsInvalidStateTransitionException()
    {
        // Arrange
        var job = new FileJob(_testJobId, _testSourcePath, _testFileSize, [_targetA]);
        job.MarkAsQueued();
        job.MarkAsInProgress();

        // Act & Assert
        var ex = Assert.Throws<InvalidStateTransitionException>(() => job.MarkAsQueued());
        Assert.Equal(JobState.InProgress.ToString(), ex.FromState);
        Assert.Equal(JobState.Queued.ToString(), ex.ToState);
    }

    [Fact]
    public void ValidStateTransitions_FollowStateMachine()
    {
        // Arrange
        var job = new FileJob(_testJobId, _testSourcePath, _testFileSize, [_targetA]);

        // Act & Assert - Valid progression
        Assert.Equal(JobState.Discovered, job.State);

        job.MarkAsQueued();
        Assert.Equal(JobState.Queued, job.State);

        job.MarkAsInProgress();
        Assert.Equal(JobState.InProgress, job.State);

        job.MarkAsPartial();
        Assert.Equal(JobState.Partial, job.State);

        job.MarkAsVerified();
        Assert.Equal(JobState.Verified, job.State);

        // Terminal state - no further transitions allowed
        Assert.Throws<InvalidStateTransitionException>(() => job.MarkAsQueued());
        Assert.Throws<InvalidStateTransitionException>(() => job.MarkAsFailed());
    }

    [Fact]
    public void FailureTransitions_AllowedFromAnyNonTerminalState()
    {
        // Test failure from Discovered
        var job1 = new FileJob(FileJobId.New(), _testSourcePath, _testFileSize, [_targetA]);
        job1.MarkAsFailed();
        Assert.Equal(JobState.Failed, job1.State);

        // Test failure from Queued
        var job2 = new FileJob(FileJobId.New(), _testSourcePath, _testFileSize, [_targetA]);
        job2.MarkAsQueued();
        job2.MarkAsFailed();
        Assert.Equal(JobState.Failed, job2.State);

        // Test failure from InProgress
        var job3 = new FileJob(FileJobId.New(), _testSourcePath, _testFileSize, [_targetA]);
        job3.MarkAsQueued();
        job3.MarkAsInProgress();
        job3.MarkAsFailed();
        Assert.Equal(JobState.Failed, job3.State);
    }

    [Fact]
    public void QuarantineTransitions_AllowedFromInProgressAndPartial()
    {
        // Test quarantine from InProgress
        var job1 = new FileJob(FileJobId.New(), _testSourcePath, _testFileSize, [_targetA]);
        job1.MarkAsQueued();
        job1.MarkAsInProgress();
        job1.MarkAsQuarantined();
        Assert.Equal(JobState.Quarantined, job1.State);

        // Test quarantine from Partial
        var job2 = new FileJob(FileJobId.New(), _testSourcePath, _testFileSize, [_targetA]);
        job2.MarkAsQueued();
        job2.MarkAsInProgress();
        job2.MarkAsPartial();
        job2.MarkAsQuarantined();
        Assert.Equal(JobState.Quarantined, job2.State);
    }

    [Fact]
    public void RequeueFromQuarantine_OnlyAllowedFromQuarantined()
    {
        // Arrange
        var job = new FileJob(_testJobId, _testSourcePath, _testFileSize, [_targetA]);
        job.MarkAsQueued();
        job.MarkAsInProgress();
        job.MarkAsQuarantined();

        // Act
        job.RequeueFromQuarantine();

        // Assert
        Assert.Equal(JobState.Queued, job.State);
    }

    [Fact]
    public void RequeueFromQuarantine_FromNonQuarantinedState_ThrowsInvalidStateTransitionException()
    {
        // Arrange
        var job = new FileJob(_testJobId, _testSourcePath, _testFileSize, [_targetA]);

        // Act & Assert
        Assert.Throws<InvalidStateTransitionException>(() => job.RequeueFromQuarantine());
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(null)]
    public void SetSourceHash_InvalidHash_ThrowsArgumentException(string invalidHash)
    {
        // Arrange
        var job = new FileJob(_testJobId, _testSourcePath, _testFileSize, [_targetA]);

        // Act & Assert
        Assert.Throws<ArgumentException>(() => job.SetSourceHash(invalidHash));
    }
}
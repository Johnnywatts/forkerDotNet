using Forker.Domain;

namespace Forker.Service.Models;

/// <summary>
/// Service health and statistics response for monitoring console
/// </summary>
public record HealthResponse
{
    public required string Status { get; init; }
    public required int ProcessId { get; init; }
    public required string Uptime { get; init; }
    public required long MemoryUsageMB { get; init; }
    public required string DatabasePath { get; init; }
    public required DateTime? LastActivity { get; init; }
    public required DateTime Timestamp { get; init; }
}

/// <summary>
/// Job statistics aggregated by state
/// </summary>
public record StatsResponse
{
    public required int TotalJobs { get; init; }
    public required int Discovered { get; init; }
    public required int Queued { get; init; }
    public required int InProgress { get; init; }
    public required int Partial { get; init; }
    public required int Verified { get; init; }
    public required int Failed { get; init; }
    public required int Quarantined { get; init; }
    public required DateTime Timestamp { get; init; }
}

/// <summary>
/// Simplified job response for list views
/// </summary>
public record JobSummaryResponse
{
    public required string JobId { get; init; }
    public required string SourcePath { get; init; }
    public required long SizeBytes { get; init; }
    public required string State { get; init; }
    public required DateTime CreatedAt { get; init; }
    public required string? SourceHash { get; init; }
}

/// <summary>
/// Detailed job response with target outcomes
/// </summary>
public record JobDetailsResponse
{
    public required string JobId { get; init; }
    public required string SourcePath { get; init; }
    public required long SizeBytes { get; init; }
    public required string State { get; init; }
    public required DateTime CreatedAt { get; init; }
    public required string? SourceHash { get; init; }
    public required List<TargetOutcomeResponse> Targets { get; init; }
}

/// <summary>
/// Target outcome information for a specific destination
/// </summary>
public record TargetOutcomeResponse
{
    public required string TargetId { get; init; }
    public required string CopyState { get; init; }
    public required int Attempts { get; init; }
    public required string? Hash { get; init; }
    public required string? FinalPath { get; init; }
    public required string? LastError { get; init; }
    public required DateTime? LastTransitionAt { get; init; }
}

/// <summary>
/// Request body for requeue operation
/// </summary>
public record RequeueRequest
{
    public required List<string> JobIds { get; init; }
}

/// <summary>
/// Response from requeue operation
/// </summary>
public record RequeueResponse
{
    public required int SuccessCount { get; init; }
    public required int FailureCount { get; init; }
    public required List<string> Errors { get; init; }
    public required DateTime Timestamp { get; init; }
}

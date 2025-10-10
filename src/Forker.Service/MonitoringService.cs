using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using Forker.Domain;
using Forker.Domain.Repositories;
using Forker.Domain.Services;
using Forker.Infrastructure.Database;
using Forker.Service.Models;
using Microsoft.Extensions.Options;

namespace Forker.Service;

/// <summary>
/// HTTP service providing monitoring and management API for ForkerDotNet Console.
/// Exposes endpoints on port 8081 for database queries, statistics, and operations.
/// </summary>
public class MonitoringService : BackgroundService
{
    private readonly ILogger<MonitoringService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly DatabaseConfiguration _databaseConfig;
    private HttpListener? _listener;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private static readonly DateTime _startTime = DateTime.UtcNow;

    public MonitoringService(
        ILogger<MonitoringService> logger,
        IServiceProvider serviceProvider,
        IOptions<DatabaseConfiguration> databaseConfig)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _databaseConfig = databaseConfig.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _listener = new HttpListener();

        // Disable host header checking to allow Docker's host.docker.internal
        _listener.UnsafeConnectionNtlmAuthentication = false;
        _listener.IgnoreWriteExceptions = true;

        // Bind to localhost on port 8081
        _listener.Prefixes.Add("http://localhost:8081/");

        try
        {
            _listener.Start();
            _logger.LogInformation("Monitoring API listening on http://localhost:8081");
            _logger.LogInformation("Available endpoints:");
            _logger.LogInformation("  GET  /api/monitoring/health");
            _logger.LogInformation("  GET  /api/monitoring/stats");
            _logger.LogInformation("  GET  /api/monitoring/jobs");
            _logger.LogInformation("  GET  /api/monitoring/jobs/{{id}}");
            _logger.LogInformation("  GET  /api/monitoring/jobs/{{id}}/state-history");
            _logger.LogInformation("  POST /api/monitoring/requeue");

            while (!stoppingToken.IsCancellationRequested)
            {
                var context = await _listener.GetContextAsync();

                // Don't await - handle requests concurrently
                _ = Task.Run(async () => await HandleRequestAsync(context), stoppingToken);
            }
        }
        catch (Exception) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Monitoring service stopping");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in monitoring service");
        }
        finally
        {
            _listener?.Stop();
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;

        try
        {
            // Add CORS headers for console on localhost:5000
            response.AddHeader("Access-Control-Allow-Origin", "http://localhost:5000");
            response.AddHeader("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
            response.AddHeader("Access-Control-Allow-Headers", "Content-Type");

            // Handle preflight OPTIONS requests
            if (request.HttpMethod == "OPTIONS")
            {
                response.StatusCode = 200;
                response.Close();
                return;
            }

            var path = request.Url?.AbsolutePath ?? "";
            var method = request.HttpMethod;

            _logger.LogDebug("Monitoring API request: {Method} {Path}", method, path);

            object? responseData = path switch
            {
                "/api/monitoring/health" when method == "GET" => await GetHealthAsync(),
                "/api/monitoring/stats" when method == "GET" => await GetStatsAsync(),
                "/api/monitoring/jobs" when method == "GET" => await GetJobsAsync(request),
                var p when p.StartsWith("/api/monitoring/jobs/", StringComparison.OrdinalIgnoreCase) && p.EndsWith("/state-history", StringComparison.OrdinalIgnoreCase) && method == "GET" => await GetStateHistoryAsync(path),
                var p when p.StartsWith("/api/monitoring/jobs/", StringComparison.OrdinalIgnoreCase) && method == "GET" => await GetJobDetailsAsync(path),
                "/api/monitoring/requeue" when method == "POST" => await RequeueJobsAsync(request),
                _ => null
            };

            if (responseData != null)
            {
                await SendJsonResponseAsync(response, responseData, 200);
            }
            else
            {
                await SendErrorResponseAsync(response, 404, "Endpoint not found");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling monitoring request: {Path}", request.Url?.AbsolutePath);
            await SendErrorResponseAsync(response, 500, $"Internal server error: {ex.Message}");
        }
    }

    private async Task<HealthResponse> GetHealthAsync()
    {
        using var scope = _serviceProvider.CreateScope();

        var process = Process.GetCurrentProcess();
        var uptime = DateTime.UtcNow - _startTime;
        var memoryMB = process.WorkingSet64 / (1024 * 1024);

        // Extract database path from connection string
        var dbPath = ExtractDatabasePath(_databaseConfig.ConnectionString);

        // Get last job activity time
        var jobRepo = scope.ServiceProvider.GetRequiredService<IJobRepository>();
        var recentJobs = await jobRepo.GetByStateAsync(JobState.InProgress);
        var lastActivity = recentJobs.OrderByDescending(j => j.CreatedAt).FirstOrDefault()?.CreatedAt;

        return new HealthResponse
        {
            Status = "healthy",
            ProcessId = process.Id,
            Uptime = FormatUptime(uptime),
            MemoryUsageMB = memoryMB,
            DatabasePath = dbPath,
            LastActivity = lastActivity,
            Timestamp = DateTime.UtcNow
        };
    }

    private async Task<StatsResponse> GetStatsAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var jobRepo = scope.ServiceProvider.GetRequiredService<IJobRepository>();

        var counts = await jobRepo.GetJobCountsByStateAsync();

        return new StatsResponse
        {
            TotalJobs = counts.Values.Sum(),
            Discovered = counts.GetValueOrDefault(JobState.Discovered, 0),
            Queued = counts.GetValueOrDefault(JobState.Queued, 0),
            InProgress = counts.GetValueOrDefault(JobState.InProgress, 0),
            Partial = counts.GetValueOrDefault(JobState.Partial, 0),
            Verified = counts.GetValueOrDefault(JobState.Verified, 0),
            Failed = counts.GetValueOrDefault(JobState.Failed, 0),
            Quarantined = counts.GetValueOrDefault(JobState.Quarantined, 0),
            Timestamp = DateTime.UtcNow
        };
    }

    private async Task<List<JobSummaryResponse>> GetJobsAsync(HttpListenerRequest request)
    {
        using var scope = _serviceProvider.CreateScope();
        var jobRepo = scope.ServiceProvider.GetRequiredService<IJobRepository>();

        // Parse query parameters
        var query = request.Url?.Query ?? "";
        var stateParam = GetQueryParameter(query, "state");
        var limitParam = GetQueryParameter(query, "limit");

        List<FileJob> jobs;

        if (!string.IsNullOrEmpty(stateParam) && Enum.TryParse<JobState>(stateParam, true, out var state))
        {
            jobs = (await jobRepo.GetByStateAsync(state)).ToList();
        }
        else
        {
            // Get all jobs - we'll need to query each state
            var allJobs = new List<FileJob>();
            foreach (JobState jobState in Enum.GetValues<JobState>())
            {
                allJobs.AddRange(await jobRepo.GetByStateAsync(jobState));
            }
            jobs = allJobs;
        }

        // Apply limit
        if (!string.IsNullOrEmpty(limitParam) && int.TryParse(limitParam, out var limit) && limit > 0)
        {
            jobs = jobs.OrderByDescending(j => j.CreatedAt).Take(limit).ToList();
        }

        return jobs.Select(j => new JobSummaryResponse
        {
            JobId = j.Id.Value.ToString(),
            SourcePath = j.SourcePath,
            SizeBytes = j.InitialSize,
            State = j.State.ToString(),
            CreatedAt = j.CreatedAt,
            SourceHash = j.SourceHash
        }).ToList();
    }

    private async Task<JobDetailsResponse?> GetJobDetailsAsync(string path)
    {
        using var scope = _serviceProvider.CreateScope();
        var jobRepo = scope.ServiceProvider.GetRequiredService<IJobRepository>();
        var targetRepo = scope.ServiceProvider.GetRequiredService<ITargetOutcomeRepository>();

        // Extract job ID from path: /api/monitoring/jobs/{id}
        var jobIdStr = path.Substring("/api/monitoring/jobs/".Length);
        if (!Guid.TryParse(jobIdStr, out var jobGuid))
        {
            return null;
        }
        var jobId = FileJobId.From(jobGuid);

        var job = await jobRepo.GetByIdAsync(jobId);
        if (job == null)
        {
            return null;
        }

        var targets = await targetRepo.GetByJobIdAsync(jobId);

        return new JobDetailsResponse
        {
            JobId = job.Id.Value.ToString(),
            SourcePath = job.SourcePath,
            SizeBytes = job.InitialSize,
            State = job.State.ToString(),
            CreatedAt = job.CreatedAt,
            SourceHash = job.SourceHash,
            Targets = targets.Select(t => new TargetOutcomeResponse
            {
                TargetId = t.TargetId.Value.ToString(),
                CopyState = t.CopyState.ToString(),
                Attempts = t.Attempts,
                Hash = t.Hash,
                FinalPath = t.FinalPath,
                LastError = t.LastError,
                LastTransitionAt = t.LastTransitionAt
            }).ToList()
        };
    }

    private async Task<List<StateChangeLogResponse>?> GetStateHistoryAsync(string path)
    {
        using var scope = _serviceProvider.CreateScope();
        var stateChangeLogger = scope.ServiceProvider.GetRequiredService<IStateChangeLogger>();

        // Extract job ID from path: /api/monitoring/jobs/{id}/state-history
        var pathPrefix = "/api/monitoring/jobs/";
        var pathSuffix = "/state-history";
        if (!path.StartsWith(pathPrefix, StringComparison.OrdinalIgnoreCase) ||
            !path.EndsWith(pathSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var jobIdStr = path.Substring(pathPrefix.Length, path.Length - pathPrefix.Length - pathSuffix.Length);
        if (string.IsNullOrEmpty(jobIdStr))
        {
            return null;
        }

        var history = await stateChangeLogger.GetJobHistoryAsync(jobIdStr);

        return history.Select(entry => new StateChangeLogResponse
        {
            Id = entry.Id,
            JobId = entry.JobId,
            EntityType = entry.EntityType,
            EntityId = entry.EntityId,
            OldState = entry.OldState,
            NewState = entry.NewState,
            Timestamp = entry.Timestamp,
            DurationMs = entry.DurationMs,
            AdditionalContext = entry.AdditionalContext
        }).ToList();
    }

    private async Task<RequeueResponse> RequeueJobsAsync(HttpListenerRequest request)
    {
        using var scope = _serviceProvider.CreateScope();
        var jobRepo = scope.ServiceProvider.GetRequiredService<IJobRepository>();

        // Read request body
        using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
        var body = await reader.ReadToEndAsync();
        var requeueRequest = JsonSerializer.Deserialize<RequeueRequest>(body, JsonOptions);

        if (requeueRequest == null || requeueRequest.JobIds.Count == 0)
        {
            throw new ArgumentException("JobIds list is required and cannot be empty");
        }

        var successCount = 0;
        var failureCount = 0;
        var errors = new List<string>();

        foreach (var jobIdStr in requeueRequest.JobIds)
        {
            try
            {
                if (!Guid.TryParse(jobIdStr, out var jobGuid))
                {
                    errors.Add($"Job {jobIdStr} is not a valid GUID");
                    failureCount++;
                    continue;
                }
                var jobId = FileJobId.From(jobGuid);
                var job = await jobRepo.GetByIdAsync(jobId);

                if (job == null)
                {
                    errors.Add($"Job {jobIdStr} not found");
                    failureCount++;
                    continue;
                }

                // Only allow requeuing failed or quarantined jobs
                if (job.State != JobState.Failed && job.State != JobState.Quarantined)
                {
                    errors.Add($"Job {jobIdStr} is in state {job.State}, can only requeue Failed or Quarantined jobs");
                    failureCount++;
                    continue;
                }

                // Transition back to Queued state
                // Note: This is a simplified implementation
                // In production, you'd need to:
                // 1. Reset target outcomes
                // 2. Move files back to input folder
                // 3. Clear error states
                // For now, just log the operation
                _logger.LogInformation("Requeue request for job {JobId} (current state: {State})", jobIdStr, job.State);

                // TODO: Implement actual requeue logic in Phase 3
                errors.Add($"Job {jobIdStr}: Requeue operation not yet fully implemented (Phase 3)");
                failureCount++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error requeuing job {JobId}", jobIdStr);
                errors.Add($"Job {jobIdStr}: {ex.Message}");
                failureCount++;
            }
        }

        return new RequeueResponse
        {
            SuccessCount = successCount,
            FailureCount = failureCount,
            Errors = errors,
            Timestamp = DateTime.UtcNow
        };
    }

    private static async Task SendJsonResponseAsync(HttpListenerResponse response, object data, int statusCode)
    {
        response.StatusCode = statusCode;
        response.ContentType = "application/json";

        var json = JsonSerializer.Serialize(data, JsonOptions);
        var buffer = Encoding.UTF8.GetBytes(json);
        response.ContentLength64 = buffer.Length;

        await response.OutputStream.WriteAsync(buffer);
        response.Close();
    }

    private static async Task SendErrorResponseAsync(HttpListenerResponse response, int statusCode, string message)
    {
        var error = new { error = message, timestamp = DateTime.UtcNow };
        await SendJsonResponseAsync(response, error, statusCode);
    }

    private static string GetQueryParameter(string query, string key)
    {
        if (string.IsNullOrEmpty(query)) return string.Empty;

        var pairs = query.TrimStart('?').Split('&');
        foreach (var pair in pairs)
        {
            var parts = pair.Split('=');
            if (parts.Length == 2 && parts[0].Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                return Uri.UnescapeDataString(parts[1]);
            }
        }
        return string.Empty;
    }

    private static string ExtractDatabasePath(string connectionString)
    {
        // Parse "Data Source=C:\ForkerDemo\forker.db" format
        var parts = connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            var keyValue = part.Split('=', 2, StringSplitOptions.TrimEntries);
            if (keyValue.Length == 2 && keyValue[0].Equals("Data Source", StringComparison.OrdinalIgnoreCase))
            {
                return keyValue[1];
            }
        }
        return connectionString; // Fallback
    }

    private static string FormatUptime(TimeSpan uptime)
    {
        if (uptime.TotalDays >= 1)
            return $"{uptime.Days}d {uptime.Hours}h {uptime.Minutes}m";
        if (uptime.TotalHours >= 1)
            return $"{uptime.Hours}h {uptime.Minutes}m";
        if (uptime.TotalMinutes >= 1)
            return $"{uptime.Minutes}m {uptime.Seconds}s";
        return $"{uptime.Seconds}s";
    }

    public override void Dispose()
    {
        try
        {
            if (_listener?.IsListening == true)
            {
                _listener.Stop();
            }
            _listener?.Close();
        }
        catch (ObjectDisposedException)
        {
            // Already disposed, ignore
        }
        GC.SuppressFinalize(this);
        base.Dispose();
    }
}

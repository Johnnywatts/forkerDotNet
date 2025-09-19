namespace Forker.Service;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;

    public Worker(ILogger<Worker> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Forker Worker Service started - Phase 1 skeleton");

        while (!stoppingToken.IsCancellationRequested)
        {
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("Worker heartbeat at: {Time}", DateTimeOffset.Now);
            }
            await Task.Delay(30000, stoppingToken); // Log every 30 seconds instead of every second
        }

        _logger.LogInformation("Forker Worker Service stopping");
    }
}

using System.Net;
using System.Text;
using System.Globalization;

namespace Forker.Service;

public class HealthService : BackgroundService
{
    private readonly ILogger<HealthService> _logger;
    private HttpListener? _listener;
    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
    };

    public HealthService(ILogger<HealthService> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _listener = new HttpListener();
        _listener.Prefixes.Add("http://localhost:8080/");

        try
        {
            _listener.Start();
            _logger.LogInformation("Health endpoint listening on http://localhost:8080/health/live");

            while (!stoppingToken.IsCancellationRequested)
            {
                var context = await _listener.GetContextAsync();
                await HandleRequest(context);
            }
        }
        catch (Exception) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Health service stopping");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in health service");
        }
        finally
        {
            _listener?.Stop();
        }
    }

    private async Task HandleRequest(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;

        try
        {
            if (request.Url?.AbsolutePath == "/health/live")
            {
                var healthStatus = new
                {
                    status = "healthy",
                    timestamp = DateTime.UtcNow,
                    service = "Forker.Service",
                    version = "1.0.0-Phase1"
                };

                var json = System.Text.Json.JsonSerializer.Serialize(healthStatus, JsonOptions);

                response.StatusCode = 200;
                response.ContentType = "application/json";

                var buffer = Encoding.UTF8.GetBytes(json);
                response.ContentLength64 = buffer.Length;

                await response.OutputStream.WriteAsync(buffer.AsMemory(0, buffer.Length));
                response.OutputStream.Close();

                _logger.LogDebug("Health check returned: {Status}", "healthy");
            }
            else
            {
                response.StatusCode = 404;
                response.OutputStream.Close();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling health request");
            response.StatusCode = 500;
            response.OutputStream.Close();
        }
    }

    public override void Dispose()
    {
        _listener?.Stop();
        _listener?.Close();
        GC.SuppressFinalize(this);
        base.Dispose();
    }
}
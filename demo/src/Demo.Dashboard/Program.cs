using Demo.Dashboard.Hubs;
using Demo.Dashboard.Services;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .WriteTo.File("logs/dashboard-.txt", rollingInterval: RollingInterval.Day)
    .CreateLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // Add Serilog
    builder.Services.AddSerilog();

    // Add services
    builder.Services.AddSignalR();
    builder.Services.AddSingleton<IFileMonitoringService, FileMonitoringService>();
    builder.Services.AddSingleton<ISystemMetricsService, SystemMetricsService>();
    builder.Services.AddHostedService<DashboardUpdateService>();

    // Add CORS for development
    builder.Services.AddCors(options =>
    {
        options.AddDefaultPolicy(policy =>
        {
            policy.AllowAnyOrigin()
                  .AllowAnyHeader()
                  .AllowAnyMethod();
        });
    });

    var app = builder.Build();

    // Configure middleware
    app.UseCors();
    app.UseStaticFiles();
    app.UseRouting();

    // Configure SignalR hub
    app.MapHub<DashboardHub>("/dashboardHub");

    // Serve dashboard page
    app.MapGet("/", () => Results.Content(GetDashboardHtml(), "text/html"));

    Log.Information("ForkerDotNet Demo Dashboard starting on http://localhost:5000");
    app.Run("http://localhost:5000");
}
catch (Exception ex)
{
    Log.Fatal(ex, "Dashboard failed to start");
}
finally
{
    Log.CloseAndFlush();
}

static string GetDashboardHtml()
{
    return """
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>ForkerDotNet Demo Dashboard</title>
    <script src="https://cdn.jsdelivr.net/npm/@microsoft/signalr@7.0.0/dist/browser/signalr.min.js"></script>
    <style>
        * { margin: 0; padding: 0; box-sizing: border-box; }
        body {
            font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
            background: #f0f2f5;
            color: #333;
        }
        .container {
            max-width: 1400px;
            margin: 0 auto;
            padding: 20px;
        }
        .header {
            background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
            color: white;
            padding: 20px;
            border-radius: 10px;
            margin-bottom: 20px;
            text-align: center;
        }
        .metrics-grid {
            display: grid;
            grid-template-columns: repeat(auto-fit, minmax(300px, 1fr));
            gap: 20px;
            margin-bottom: 20px;
        }
        .card {
            background: white;
            border-radius: 10px;
            padding: 20px;
            box-shadow: 0 2px 10px rgba(0,0,0,0.1);
        }
        .card h3 {
            color: #667eea;
            margin-bottom: 15px;
            border-bottom: 2px solid #eee;
            padding-bottom: 5px;
        }
        .metric-value {
            font-size: 2em;
            font-weight: bold;
            color: #2c3e50;
        }
        .metric-label {
            color: #7f8c8d;
            font-size: 0.9em;
            margin-top: 5px;
        }
        .progress-bar {
            width: 100%;
            height: 20px;
            background: #ecf0f1;
            border-radius: 10px;
            overflow: hidden;
            margin: 10px 0;
        }
        .progress-fill {
            height: 100%;
            background: linear-gradient(90deg, #27ae60, #2ecc71);
            border-radius: 10px;
            transition: width 0.3s ease;
        }
        .status-indicator {
            display: inline-block;
            width: 12px;
            height: 12px;
            border-radius: 50%;
            margin-right: 8px;
        }
        .status-healthy { background: #27ae60; }
        .status-warning { background: #f39c12; }
        .status-error { background: #e74c3c; }
        .event-log {
            background: #2c3e50;
            color: #ecf0f1;
            border-radius: 10px;
            padding: 20px;
            max-height: 400px;
            overflow-y: auto;
            font-family: 'Courier New', monospace;
        }
        .log-entry {
            margin: 5px 0;
            padding: 5px;
            border-left: 3px solid #3498db;
            padding-left: 10px;
        }
        .log-error { border-left-color: #e74c3c; }
        .log-warning { border-left-color: #f39c12; }
        .log-success { border-left-color: #27ae60; }
        @media (max-width: 768px) {
            .metrics-grid { grid-template-columns: 1fr; }
        }
    </style>
</head>
<body>
    <div class="container">
        <div class="header">
            <h1>🏥 ForkerDotNet Clinical Demo Dashboard</h1>
            <p>Real-time monitoring of medical imaging file processing</p>
        </div>

        <div class="metrics-grid">
            <div class="card">
                <h3>📁 File Processing Status</h3>
                <div class="metric-value" id="filesInQueue">0</div>
                <div class="metric-label">Files in Queue</div>
                <div class="progress-bar">
                    <div class="progress-fill" id="processingProgress" style="width: 0%"></div>
                </div>
                <div id="processingStatus">Idle</div>
            </div>

            <div class="card">
                <h3>🎯 Directory Status</h3>
                <div>Input: <span id="inputCount">0</span> files</div>
                <div>Destination A: <span id="destACount">0</span> files</div>
                <div>Destination B: <span id="destBCount">0</span> files</div>
                <div>Archive: <span id="archiveCount">0</span> files</div>
                <div>Quarantine: <span id="quarantineCount">0</span> files</div>
            </div>

            <div class="card">
                <h3>⚡ System Performance</h3>
                <div>Memory: <span id="memoryUsage">0</span> MB</div>
                <div>CPU: <span id="cpuUsage">0</span>%</div>
                <div>Throughput: <span id="throughput">0</span> MB/min</div>
                <div class="progress-bar">
                    <div class="progress-fill" id="memoryProgress" style="width: 0%"></div>
                </div>
            </div>

            <div class="card">
                <h3>🔒 Safety Indicators</h3>
                <div>
                    <span class="status-indicator status-healthy" id="integrityStatus"></span>
                    Data Integrity: <span id="integrityText">100%</span>
                </div>
                <div>
                    <span class="status-indicator status-healthy" id="serviceStatus"></span>
                    Service Health: <span id="serviceText">Operational</span>
                </div>
                <div>
                    <span class="status-indicator status-healthy" id="verificationStatus"></span>
                    Hash Verification: <span id="verificationText">Passing</span>
                </div>
            </div>
        </div>

        <div class="card">
            <h3>📝 Live Event Log</h3>
            <div class="event-log" id="eventLog">
                <div class="log-entry">Dashboard initialized - waiting for events...</div>
            </div>
        </div>
    </div>

    <script>
        const connection = new signalR.HubConnectionBuilder()
            .withUrl("/dashboardHub")
            .build();

        // Start connection
        connection.start().then(function () {
            console.log("Connected to dashboard hub");
            addLogEntry("Connected to ForkerDotNet service", "success");
        }).catch(function (err) {
            console.error(err.toString());
            addLogEntry("Failed to connect to service: " + err, "error");
        });

        // Update file counts
        connection.on("UpdateFileCounts", function (counts) {
            document.getElementById("inputCount").textContent = counts.input;
            document.getElementById("destACount").textContent = counts.destinationA;
            document.getElementById("destBCount").textContent = counts.destinationB;
            document.getElementById("archiveCount").textContent = counts.archive;
            document.getElementById("quarantineCount").textContent = counts.quarantine;
        });

        // Update system metrics
        connection.on("UpdateSystemMetrics", function (metrics) {
            document.getElementById("memoryUsage").textContent = metrics.memoryMB;
            document.getElementById("cpuUsage").textContent = metrics.cpuPercent;
            document.getElementById("throughput").textContent = metrics.throughputMBPerMin;

            const memoryProgress = Math.min((metrics.memoryMB / 2048) * 100, 100);
            document.getElementById("memoryProgress").style.width = memoryProgress + "%";
        });

        // Update processing status
        connection.on("UpdateProcessingStatus", function (status) {
            document.getElementById("filesInQueue").textContent = status.queueDepth;
            document.getElementById("processingStatus").textContent = status.currentOperation;

            const progress = status.queueDepth > 0 ?
                Math.min(((status.processedCount / (status.processedCount + status.queueDepth)) * 100), 100) : 0;
            document.getElementById("processingProgress").style.width = progress + "%";
        });

        // File processing events
        connection.on("FileEvent", function (event) {
            const timestamp = new Date().toLocaleTimeString();
            const message = `${timestamp} - ${event.operation}: ${event.fileName} (${event.status})`;
            addLogEntry(message, event.status.toLowerCase());
        });

        // Safety status updates
        connection.on("UpdateSafetyStatus", function (safety) {
            updateSafetyIndicator("integrityStatus", "integrityText", safety.dataIntegrity);
            updateSafetyIndicator("serviceStatus", "serviceText", safety.serviceHealth);
            updateSafetyIndicator("verificationStatus", "verificationText", safety.hashVerification);
        });

        function addLogEntry(message, type = "info") {
            const logContainer = document.getElementById("eventLog");
            const entry = document.createElement("div");
            entry.className = `log-entry log-${type}`;
            entry.textContent = message;

            logContainer.appendChild(entry);
            logContainer.scrollTop = logContainer.scrollHeight;

            // Limit to last 50 entries
            while (logContainer.children.length > 50) {
                logContainer.removeChild(logContainer.firstChild);
            }
        }

        function updateSafetyIndicator(indicatorId, textId, status) {
            const indicator = document.getElementById(indicatorId);
            const text = document.getElementById(textId);

            indicator.className = `status-indicator status-${status.status}`;
            text.textContent = status.text;
        }

        // Simulate some initial activity
        setTimeout(() => {
            addLogEntry("File monitoring service started", "success");
            addLogEntry("Scanning directories for existing files", "info");
        }, 1000);
    </script>
</body>
</html>
""";
}
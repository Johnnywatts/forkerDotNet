package server

import (
	"context"
	"encoding/json"
	"fmt"
	"log"
	"net/http"
	"time"

	"forkerDotNet/console/internal/demo"
)

// handleDemoPage renders the main Demo Mode page
func handleDemoPage(w http.ResponseWriter, r *http.Request) {
	w.Header().Set("Content-Type", "text/html")

	html := `<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>Demo Mode - ForkerDotNet Console</title>
    <script src="https://unpkg.com/htmx.org@1.9.10"></script>
    <link rel="stylesheet" href="/static/style.css">
    <style>
        .demo-container {
            max-width: 1400px;
            margin: 20px auto;
            padding: 20px;
        }
        .demo-grid {
            display: grid;
            grid-template-columns: 40% 60%;
            gap: 20px;
            margin-top: 20px;
        }
        .demo-panel {
            border: 1px solid #ddd;
            border-radius: 8px;
            padding: 20px;
            background: #f9f9f9;
        }
        .demo-panel h3 {
            margin: 0 0 15px 0;
            color: #333;
            font-size: 1.2em;
            border-bottom: 2px solid #0066cc;
            padding-bottom: 10px;
        }
        .preflight-checks {
            margin-top: 15px;
        }
        .check-item {
            display: flex;
            align-items: center;
            gap: 10px;
            padding: 8px;
            margin: 5px 0;
            background: white;
            border-radius: 4px;
            border: 1px solid #e0e0e0;
        }
        .check-status {
            font-size: 1.2em;
            min-width: 30px;
        }
        .check-name {
            flex: 1;
            font-weight: 500;
        }
        .check-message {
            color: #666;
            font-size: 0.9em;
        }
        .check-duration {
            color: #999;
            font-size: 0.85em;
        }
        .btn {
            padding: 10px 20px;
            border: none;
            border-radius: 4px;
            font-size: 1em;
            cursor: pointer;
            margin: 5px;
            transition: background 0.3s;
        }
        .btn-primary {
            background: #0066cc;
            color: white;
        }
        .btn-primary:hover {
            background: #004499;
        }
        .btn-secondary {
            background: #6c757d;
            color: white;
        }
        .btn-secondary:hover {
            background: #545b62;
        }
        .btn-success {
            background: #28a745;
            color: white;
        }
        .btn-success:hover {
            background: #218838;
        }
        .btn:disabled {
            background: #ccc;
            cursor: not-allowed;
        }
        .scenario-btn {
            display: block;
            width: 100%;
            text-align: left;
            padding: 15px;
            margin: 10px 0;
            background: white;
            border: 2px solid #0066cc;
            border-radius: 6px;
            cursor: pointer;
            transition: all 0.3s;
        }
        .scenario-btn:hover:not(:disabled) {
            background: #e6f2ff;
            transform: translateX(5px);
        }
        .scenario-btn:disabled {
            border-color: #ccc;
            color: #999;
            cursor: not-allowed;
        }
        .scenario-title {
            font-weight: 600;
            font-size: 1.1em;
            color: #0066cc;
            margin-bottom: 5px;
        }
        .scenario-desc {
            font-size: 0.9em;
            color: #666;
        }
        .status-badge {
            display: inline-block;
            padding: 4px 10px;
            border-radius: 4px;
            font-size: 0.85em;
            font-weight: 600;
            margin: 10px 0;
        }
        .status-ready {
            background: #d4edda;
            color: #155724;
        }
        .status-error {
            background: #f8d7da;
            color: #721c24;
        }
        .status-warning {
            background: #fff3cd;
            color: #856404;
        }
        .progress-panel {
            margin-top: 20px;
            padding: 15px;
            background: #f0f0f0;
            border-radius: 6px;
            min-height: 200px;
        }
        .progress-message {
            padding: 8px;
            margin: 5px 0;
            background: white;
            border-left: 3px solid #0066cc;
            font-family: 'Courier New', monospace;
            font-size: 0.9em;
        }
        .loading {
            text-align: center;
            padding: 20px;
            color: #666;
        }
        .spinner {
            display: inline-block;
            width: 20px;
            height: 20px;
            border: 3px solid #f3f3f3;
            border-top: 3px solid #0066cc;
            border-radius: 50%;
            animation: spin 1s linear infinite;
        }
        @keyframes spin {
            0% { transform: rotate(0deg); }
            100% { transform: rotate(360deg); }
        }
    </style>
</head>
<body>
    <header>
        <h1>ForkerDotNet Console</h1>
        <nav>
            <a href="/">Dashboard</a>
            <a href="/folders">Folders</a>
            <a href="/transactions">Transactions</a>
            <a href="/demo" class="active">Demo Mode</a>
        </nav>
    </header>
    <main>
        <div class="demo-container">
            <h2>Demo Mode - CCSO Presentation</h2>
            <p style="color: #666;">Orchestrate PowerShell demo scenarios with real-time monitoring and safety checks.</p>

            <div class="demo-grid">
                <!-- Left Panel: Pre-Flight & Scenarios -->
                <div>
                    <div class="demo-panel">
                        <h3>Pre-Flight Validation</h3>
                        <button class="btn btn-primary" onclick="runPreFlightChecks()">
                            <span id="preflight-btn-text">Run Pre-Flight Checks</span>
                        </button>
                        <div id="preflight-summary"></div>
                        <div id="preflight-checks" class="preflight-checks"></div>
                    </div>

                    <div class="demo-panel" style="margin-top: 20px;">
                        <h3>Scenario Launcher</h3>
                        <p style="font-size: 0.9em; color: #666;">Run pre-flight checks before launching scenarios</p>

                        <button class="scenario-btn" onclick="runScenario(1)" disabled id="scenario-1-btn">
                            <div class="scenario-title">▶ Scenario 1: End-to-End</div>
                            <div class="scenario-desc">Complete file copy workflow with verification (~5 min)</div>
                        </button>

                        <button class="scenario-btn" onclick="runScenario(2)" disabled id="scenario-2-btn">
                            <div class="scenario-title">▶ Scenario 2: Corruption Detection</div>
                            <div class="scenario-desc">Hash mismatch detection and quarantine (~4 min)</div>
                        </button>

                        <button class="scenario-btn" onclick="runScenario(3)" disabled id="scenario-3-btn">
                            <div class="scenario-title">▶ Scenario 3: Concurrent Access</div>
                            <div class="scenario-desc">Non-locking file operations proof (~5 min)</div>
                        </button>

                        <button class="scenario-btn" onclick="runScenario(4)" disabled id="scenario-4-btn">
                            <div class="scenario-title">▶ Scenario 4: Crash Recovery</div>
                            <div class="scenario-desc">Service crash and automatic recovery (~5 min) [Admin Required]</div>
                        </button>

                        <button class="scenario-btn" onclick="runScenario(5)" disabled id="scenario-5-btn">
                            <div class="scenario-title">▶ Scenario 5: Stability Detection</div>
                            <div class="scenario-desc">Growing file detection and wait (~4 min)</div>
                        </button>
                    </div>
                </div>

                <!-- Right Panel: Progress & Status -->
                <div>
                    <div class="demo-panel">
                        <h3>Scenario Progress</h3>
                        <div id="scenario-status">
                            <p style="color: #666; text-align: center; padding: 40px;">
                                No scenario running. Select a scenario from the left panel to begin.
                            </p>
                        </div>
                        <div id="scenario-progress" class="progress-panel" style="display: none;">
                            <div id="progress-messages"></div>
                        </div>
                    </div>
                </div>
            </div>
        </div>
    </main>
    <script>
        let preFlightPassed = false;

        // Run pre-flight checks
        async function runPreFlightChecks() {
            const btn = document.getElementById('preflight-btn-text');
            const summaryDiv = document.getElementById('preflight-summary');
            const checksDiv = document.getElementById('preflight-checks');

            btn.innerHTML = '<span class="spinner"></span> Running checks...';
            summaryDiv.innerHTML = '';
            checksDiv.innerHTML = '';

            try {
                const response = await fetch('/api/demo/preflight');
                if (!response.ok) {
                    throw new Error('Pre-flight checks failed: ' + response.statusText);
                }

                const result = await response.json();
                renderPreFlightResults(result);
                preFlightPassed = result.can_execute;

                // Enable/disable scenario buttons based on result
                updateScenarioButtons(result.can_execute);

            } catch (error) {
                console.error('Pre-flight error:', error);
                summaryDiv.innerHTML = '<div class="status-badge status-error">Error: ' + error.message + '</div>';
            } finally {
                btn.textContent = 'Run Pre-Flight Checks';
            }
        }

        // Render pre-flight results
        function renderPreFlightResults(result) {
            const summaryDiv = document.getElementById('preflight-summary');
            const checksDiv = document.getElementById('preflight-checks');

            // Summary badge
            let badgeClass = result.can_execute ? 'status-ready' : 'status-error';
            if (result.warnings > 0 && result.can_execute) {
                badgeClass = 'status-warning';
            }

            summaryDiv.innerHTML = ` + "`" + `
                <div class="status-badge ${badgeClass}">
                    ${result.summary}
                </div>
                <div style="font-size: 0.9em; color: #666; margin-top: 10px;">
                    ${result.passed} passed, ${result.failed} failed, ${result.warnings} warnings
                </div>
            ` + "`" + `;

            // Individual checks
            let checksHTML = '';
            result.checks.forEach(check => {
                let icon = '✓';
                let color = '#28a745';
                if (check.status === 'fail') {
                    icon = '✗';
                    color = '#dc3545';
                } else if (check.status === 'warning') {
                    icon = '⚠';
                    color = '#ffc107';
                }

                checksHTML += ` + "`" + `
                    <div class="check-item">
                        <span class="check-status" style="color: ${color};">${icon}</span>
                        <div style="flex: 1;">
                            <div class="check-name">${check.name}</div>
                            <div class="check-message">${check.message}</div>
                        </div>
                        <span class="check-duration">${check.duration_ms}ms</span>
                    </div>
                ` + "`" + `;
            });

            checksDiv.innerHTML = checksHTML;
        }

        // Update scenario button states
        function updateScenarioButtons(canExecute) {
            for (let i = 1; i <= 5; i++) {
                const btn = document.getElementById(` + "`scenario-${i}-btn`" + `);
                if (btn) {
                    btn.disabled = !canExecute;
                }
            }
        }

        // Run scenario
        async function runScenario(scenarioNum) {
            if (!preFlightPassed) {
                alert('Please run pre-flight checks first and ensure they pass');
                return;
            }

            const statusDiv = document.getElementById('scenario-status');
            const progressDiv = document.getElementById('scenario-progress');
            const messagesDiv = document.getElementById('progress-messages');

            // Show progress panel
            statusDiv.style.display = 'none';
            progressDiv.style.display = 'block';
            messagesDiv.innerHTML = '<div class="progress-message">Starting scenario ' + scenarioNum + '...</div>';

            // Disable all scenario buttons during execution
            updateScenarioButtons(false);

            try {
                // TODO: Implement SSE streaming for real-time progress
                // For now, just show a placeholder
                messagesDiv.innerHTML += '<div class="progress-message">Scenario execution not yet implemented</div>';
                messagesDiv.innerHTML += '<div class="progress-message">This will execute: scripts\\Run-Scenario' + scenarioNum + '-*.ps1</div>';

                // Simulate delay
                await new Promise(resolve => setTimeout(resolve, 2000));

                messagesDiv.innerHTML += '<div class="progress-message" style="color: #28a745;">Scenario ' + scenarioNum + ' placeholder complete</div>';

            } catch (error) {
                console.error('Scenario error:', error);
                messagesDiv.innerHTML += '<div class="progress-message" style="color: #dc3545;">Error: ' + error.message + '</div>';
            } finally {
                // Re-enable buttons
                updateScenarioButtons(preFlightPassed);
            }
        }

        // Auto-run pre-flight on page load
        document.addEventListener('DOMContentLoaded', function() {
            runPreFlightChecks();
        });
    </script>
</body>
</html>`;

	w.Write([]byte(html))
}

// handlePreFlightAPI runs pre-flight validation checks
func handlePreFlightAPI(w http.ResponseWriter, r *http.Request) {
	client := GetAPIClient()
	validator := demo.NewPreFlightValidator(client)

	ctx, cancel := context.WithTimeout(r.Context(), 30*time.Second)
	defer cancel()

	result := validator.RunAllChecks(ctx)

	w.Header().Set("Content-Type", "application/json")
	if err := json.NewEncoder(w).Encode(result); err != nil {
		log.Printf("[ERROR] Failed to encode pre-flight results: %v", err)
		http.Error(w, "Internal Server Error", http.StatusInternalServerError)
		return
	}
}

// handleRunScenarioAPI executes a demo scenario (placeholder for Phase 1)
func handleRunScenarioAPI(w http.ResponseWriter, r *http.Request) {
	var request struct {
		ScenarioNum int `json:"scenario_num"`
	}

	if err := json.NewDecoder(r.Body).Decode(&request); err != nil {
		http.Error(w, "Invalid request body", http.StatusBadRequest)
		return
	}

	// TODO: Implement scenario execution with SSE streaming (Task 4.2)
	// For now, just return a placeholder response
	w.Header().Set("Content-Type", "application/json")
	json.NewEncoder(w).Encode(map[string]interface{}{
		"status":  "not_implemented",
		"message": fmt.Sprintf("Scenario %d execution not yet implemented", request.ScenarioNum),
	})
}

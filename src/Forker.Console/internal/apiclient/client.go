package apiclient

import (
	"context"
	"encoding/json"
	"fmt"
	"io"
	"net/http"
	"time"
)

// Client is an HTTP client for ForkerDotNet Monitoring API
type Client struct {
	baseURL    string
	httpClient *http.Client
}

// NewClient creates a new API client
// baseURL example: "http://host.docker.internal:8081"
func NewClient(baseURL string) *Client {
	return &Client{
		baseURL: baseURL,
		httpClient: &http.Client{
			Timeout: 10 * time.Second,
		},
	}
}

// fixHostHeader overrides the Host header to "localhost:8081" for Windows HttpListener compatibility
// Windows HttpListener rejects "host.docker.internal" as an invalid hostname
func fixHostHeader(req *http.Request) {
	req.Host = "localhost:8081"
}

// Health checks the API health endpoint
func (c *Client) Health(ctx context.Context) (*HealthResponse, error) {
	url := fmt.Sprintf("%s/api/monitoring/health", c.baseURL)

	req, err := http.NewRequestWithContext(ctx, http.MethodGet, url, nil)
	if err != nil {
		return nil, fmt.Errorf("create request: %w", err)
	}

	fixHostHeader(req)

	resp, err := c.httpClient.Do(req)
	if err != nil {
		return nil, fmt.Errorf("http request: %w", err)
	}
	defer resp.Body.Close()

	if resp.StatusCode != http.StatusOK {
		body, _ := io.ReadAll(resp.Body)
		return nil, fmt.Errorf("unexpected status %d: %s", resp.StatusCode, string(body))
	}

	var health HealthResponse
	if err := json.NewDecoder(resp.Body).Decode(&health); err != nil {
		return nil, fmt.Errorf("decode response: %w", err)
	}

	return &health, nil
}

// GetStats retrieves job statistics
func (c *Client) GetStats(ctx context.Context) (*StatsResponse, error) {
	url := fmt.Sprintf("%s/api/monitoring/stats", c.baseURL)

	req, err := http.NewRequestWithContext(ctx, http.MethodGet, url, nil)
	if err != nil {
		return nil, fmt.Errorf("create request: %w", err)
	}

	fixHostHeader(req)

	resp, err := c.httpClient.Do(req)
	if err != nil {
		return nil, fmt.Errorf("http request: %w", err)
	}
	defer resp.Body.Close()

	if resp.StatusCode != http.StatusOK {
		body, _ := io.ReadAll(resp.Body)
		return nil, fmt.Errorf("unexpected status %d: %s", resp.StatusCode, string(body))
	}

	var stats StatsResponse
	if err := json.NewDecoder(resp.Body).Decode(&stats); err != nil {
		return nil, fmt.Errorf("decode response: %w", err)
	}

	return &stats, nil
}

// GetJobs retrieves job summaries, optionally filtered by state
func (c *Client) GetJobs(ctx context.Context, state string, limit int) ([]JobSummary, error) {
	url := fmt.Sprintf("%s/api/monitoring/jobs?limit=%d", c.baseURL, limit)
	if state != "" {
		url = fmt.Sprintf("%s&state=%s", url, state)
	}

	req, err := http.NewRequestWithContext(ctx, http.MethodGet, url, nil)
	if err != nil {
		return nil, fmt.Errorf("create request: %w", err)
	}

	fixHostHeader(req)

	resp, err := c.httpClient.Do(req)
	if err != nil {
		return nil, fmt.Errorf("http request: %w", err)
	}
	defer resp.Body.Close()

	if resp.StatusCode != http.StatusOK {
		body, _ := io.ReadAll(resp.Body)
		return nil, fmt.Errorf("unexpected status %d: %s", resp.StatusCode, string(body))
	}

	var jobs []JobSummary
	if err := json.NewDecoder(resp.Body).Decode(&jobs); err != nil {
		return nil, fmt.Errorf("decode response: %w", err)
	}

	return jobs, nil
}

// GetJobDetails retrieves detailed information for a specific job
func (c *Client) GetJobDetails(ctx context.Context, jobID string) (*JobDetails, error) {
	url := fmt.Sprintf("%s/api/monitoring/jobs/%s", c.baseURL, jobID)

	req, err := http.NewRequestWithContext(ctx, http.MethodGet, url, nil)
	if err != nil {
		return nil, fmt.Errorf("create request: %w", err)
	}

	resp, err := c.httpClient.Do(req)
	if err != nil {
		return nil, fmt.Errorf("http request: %w", err)
	}
	defer resp.Body.Close()

	if resp.StatusCode == http.StatusNotFound {
		return nil, nil
	}

	if resp.StatusCode != http.StatusOK {
		body, _ := io.ReadAll(resp.Body)
		return nil, fmt.Errorf("unexpected status %d: %s", resp.StatusCode, string(body))
	}

	var details JobDetails
	if err := json.NewDecoder(resp.Body).Decode(&details); err != nil {
		return nil, fmt.Errorf("decode response: %w", err)
	}

	return &details, nil
}

// RequeueJob requeues a failed job for retry
func (c *Client) RequeueJob(ctx context.Context, jobID string) (*RequeueResponse, error) {
	url := fmt.Sprintf("%s/api/monitoring/requeue", c.baseURL)

	reqBody := RequeueRequest{
		JobID: jobID,
	}

	bodyBytes, err := json.Marshal(reqBody)
	if err != nil {
		return nil, fmt.Errorf("marshal request: %w", err)
	}

	req, err := http.NewRequestWithContext(ctx, http.MethodPost, url, nil)
	if err != nil {
		return nil, fmt.Errorf("create request: %w", err)
	}
	req.Header.Set("Content-Type", "application/json")

	// Attach body (placeholder for now - MonitoringService needs POST body parsing)
	_ = bodyBytes

	resp, err := c.httpClient.Do(req)
	if err != nil {
		return nil, fmt.Errorf("http request: %w", err)
	}
	defer resp.Body.Close()

	if resp.StatusCode != http.StatusOK {
		body, _ := io.ReadAll(resp.Body)
		return nil, fmt.Errorf("unexpected status %d: %s", resp.StatusCode, string(body))
	}

	var result RequeueResponse
	if err := json.NewDecoder(resp.Body).Decode(&result); err != nil {
		return nil, fmt.Errorf("decode response: %w", err)
	}

	return &result, nil
}

// Ping verifies the API is reachable
func (c *Client) Ping(ctx context.Context) error {
	_, err := c.Health(ctx)
	return err
}

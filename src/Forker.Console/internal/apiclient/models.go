package apiclient

// HealthResponse matches MonitoringModels.HealthResponse
type HealthResponse struct {
	Status         string  `json:"status"`
	ProcessID      int     `json:"processId"`
	Uptime         string  `json:"uptime"`
	MemoryUsageMB  int64   `json:"memoryUsageMB"`
	DatabasePath   string  `json:"databasePath"`
	LastActivity   *string `json:"lastActivity"`
	Timestamp      string  `json:"timestamp"`
}

// StatsResponse matches MonitoringModels.StatsResponse
type StatsResponse struct {
	TotalJobs   int `json:"totalJobs"`
	Discovered  int `json:"discovered"`
	Queued      int `json:"queued"`
	InProgress  int `json:"inProgress"`
	Partial     int `json:"partial"`
	Verified    int `json:"verified"`
	Failed      int `json:"failed"`
	Quarantined int `json:"quarantined"`
}

// JobSummary matches MonitoringModels.JobSummaryResponse
type JobSummary struct {
	JobID       string  `json:"jobId"`
	SourcePath  string  `json:"sourcePath"`
	State       string  `json:"state"`
	InitialSize int64   `json:"initialSize"`
	SourceHash  *string `json:"sourceHash"`
	CreatedAt   string  `json:"createdAt"`
}

// JobDetails matches MonitoringModels.JobDetailsResponse
type JobDetails struct {
	JobID        string          `json:"jobId"`
	SourcePath   string          `json:"sourcePath"`
	State        string          `json:"state"`
	InitialSize  int64           `json:"initialSize"`
	SourceHash   *string         `json:"sourceHash"`
	CreatedAt    string          `json:"createdAt"`
	VersionToken int             `json:"versionToken"`
	Targets      []TargetOutcome `json:"targets"`
}

// TargetOutcome matches MonitoringModels.TargetOutcomeResponse
type TargetOutcome struct {
	TargetID         string  `json:"targetId"`
	State            string  `json:"state"`
	Hash             *string `json:"hash"`
	BytesCopied      *int64  `json:"bytesCopied"`
	LastTransitionAt string  `json:"lastTransitionAt"`
}

// RequeueRequest matches MonitoringModels.RequeueRequest
type RequeueRequest struct {
	JobID string `json:"jobId"`
}

// RequeueResponse matches MonitoringModels.RequeueResponse
type RequeueResponse struct {
	Success bool   `json:"success"`
	Message string `json:"message"`
	NewState string `json:"newState"`
}

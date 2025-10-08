package database

// FileJob represents a file copy job
type FileJob struct {
	ID           string
	SourcePath   string
	State        string
	InitialSize  int64
	SourceHash   *string
	CreatedAt    string // SQLite stores as TEXT
	VersionToken int
}

// TargetOutcome represents a per-target copy result
type TargetOutcome struct {
	JobID            string
	TargetID         string
	State            string
	Hash             *string
	BytesCopied      *int64
	LastTransitionAt string // SQLite stores as TEXT
}

// JobDetails combines a job with its targets
type JobDetails struct {
	Job     FileJob
	Targets []TargetOutcome
}

// Stats contains summary statistics
type Stats struct {
	TotalJobs   int
	Verified    int
	Failed      int
	Quarantined int
	Active      int
}

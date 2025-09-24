# Commercial solutions for dual-target on-prem SMB copying (NHS-friendly)

## Context and requirements
- Source: on-prem SMB share
- Targets: two on-prem SMB shares (dual destination)
- Environment: Windows service on dedicated VM(s), running under a service account (e.g., gMSA)
- Integrity: hashing used only for verification (no encryption required)
- Must-have: atomic publish across both targets (no final filename visible until both copies complete and verify)
- Files: very large (e.g., SVS/WSI); resilience, restart, and audit needed

## Summary recommendation
For minimal custom engineering with strong governance, use a Managed File Transfer (MFT) platform that supports:
- Dual/multi-destination tasks
- Copy-to-temp and rename-on-complete
- Checksum verification (SHA-256)
- Conditional gating: only publish if BOTH destinations succeed
- Auditing, RBAC, alerting, and Windows service hardening

Best-fit MFT options widely used across NHS:
- Progress MOVEit Automation
- Fortra Globalscape EFT (with Advanced Workflow Engine)

If you prefer agent-based replication over MFT and want near-real-time behavior:
- PeerGFS (Peer Software)
- Resilio Connect

Native Windows options (Robocopy/DFSR) are viable but require custom orchestration to simulate cross-target atomicity and typically lack rich audit/governance.

## Why “atomic across two SMB targets” needs orchestration
- Windows/SMB does not provide a single transaction across two remote shares.
- Practical approach:
  1) Copy the file to BOTH targets using a temporary extension (e.g., .tmp) or hidden path.
  2) Verify integrity (e.g., SHA-256) on both.
  3) Only after both succeed, rename both temp files to the final filename. If either fails, clean up and retry.
  4) Optional: create a small .ready manifest after both renames; downstream systems only act when .ready is present.

Most commercial tools above natively support temp-file writes, hash verification, and rename-on-complete; MFT tools also make the cross-target gating simple to express.

## Option details

### 1) Progress MOVEit Automation
- Strengths: Simple dual-destination UNC workflows; SHA-256 verification; temp file then rename; conditional steps to ensure both targets pass before publish; retries; audit and alerting; runs as Windows service under gMSA.
- Fit: Best balance of simplicity, auditability, and NHS procurement familiarity.

### 2) Fortra Globalscape EFT (AWE)
- Strengths: Similar to MOVEit—multi-destination tasks, checksum verification, temp+rename, gating logic, RBAC, audit, alerting.
- Fit: Equally strong choice for governed on-prem transfers.

### 3) PeerGFS (Peer Software)
- Strengths: Agent-based, resilient SMB replication to multiple targets, strong at very large files, resume, bandwidth/QoS controls.
- Atomic publish pattern: Write as temp on both targets, verify, then coordinated rename or manifest/ready gating. Confirm vendor guidance for coordinated promotion.

### 4) Resilio Connect
- Strengths: Fast peer-based replication, handles large files well, robust resume and WAN efficiency (if needed), good reporting.
- Atomic publish pattern: Same as above—temp write + verify + coordinated rename/manifest.

### 5) Native Windows tools (considered)
- Robocopy: Built-in, supports /Z (restartable) and /MT (multithread). But cannot guarantee cross-target atomic publish without custom scripting/orchestration; limited governance/audit.
- DFS Replication (DFSR): Reliable for folder replication, but does not provide a built-in cross-target commit step; handling very large single files requires proper staging sizing and tuning.
- Storage Replica: Volume/block-level DR solution, not suited for a single source-to-two-targets “atomic publish” at file level.

## Suggested reference workflow (for MFT tools)
- Task steps:
  - Copy source file to TargetA and TargetB as filename.tmp
  - Compute/verify SHA-256 for both temp files
  - If both pass: rename filename.tmp -> filename on both targets
  - If either fails: delete temp(s) and retry with backoff; raise alert on repeated failures
  - Optional: emit filename.ready (or a manifest) only after both renames succeed; downstream consumers act only when .ready is present
- Service hardening:
  - Run under gMSA/least privilege; lock down service rights and share/file ACLs
  - Enable detailed audit logs and central monitoring/alerting

## NHS procurement/compliance notes
- There is no single central “approved list” covering all software. NHS organisations typically procure via frameworks (e.g., G-Cloud, SBS), ensure DSPT compliance, and follow NCSC guidance. If the workflow touches clinical pathways, apply local clinical safety processes (e.g., DCB0129/DCB0160) as appropriate.
- The products above are commonly deployed in NHS environments; validate current framework availability and vendor assurances with your local procurement/IG teams.

## Next steps
- Confirm acceptance criteria for “atomic”: No final filename visible on either target until both copies complete and verify.
- Choose: MFT (MOVEit/Globalscape) for governance-first, or PeerGFS/Resilio for agent-based replication.
- Pilot on a dedicated VM using a gMSA, with a test folder and representative large files.
- Capture evidence: transfer logs, hash verification, rename timings, and failure/recovery tests for assurance.
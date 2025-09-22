# Race Condition Testing Design Document

## Executive Summary

This document outlines the comprehensive testing strategy for eliminating race conditions in ForkerDotNet's FileDiscoveryService, which is critical for medical imaging file processing where data loss is unacceptable. The approach uses a hybrid testing strategy spanning in-process stress testing, multi-process validation, and production-like VM simulation.

## Critical Race Conditions Identified

### 1. Timer Callback Anti-Pattern
**Location**: `FileDiscoveryService.ProcessPendingFiles()` line 237
**Issue**: `async void` method can cause overlapping executions under heavy load
**Risk**: File processing corruption, memory leaks, unhandled exceptions
**Medical Impact**: Files could be processed multiple times or lost during overlapping executions

### 2. Event Handler Race Conditions
**Location**: `FileDiscoveryService.NotifyFileDiscovered()` line 319
**Issue**: `FileDiscovered?.Invoke()` not thread-safe for subscribers with shared state
**Risk**: Event subscribers could receive corrupted data or miss events
**Medical Impact**: Critical file discovery events could be lost or duplicated

### 3. Disposal Race Conditions
**Location**: `FileDiscoveryService.Dispose()` line 333
**Issue**: `GetAwaiter().GetResult()` can cause deadlocks during shutdown
**Risk**: Service shutdown hangs, resource leaks, data corruption during disposal
**Medical Impact**: Service restarts could fail, leaving files in inconsistent states

### 4. Shutdown Sequence Issues
**Location**: `FileDiscoveryService._isRunning` checks throughout
**Issue**: Volatile bool checks not atomic with operations
**Risk**: Operations starting after shutdown initiated, inconsistent state
**Medical Impact**: Files discovered during shutdown could be lost or corrupted

## Hybrid Testing Strategy

### Phase 5.1A: In-Process NBomber Testing (Days 1-2)

**Objective**: Validate race condition fixes under controlled concurrent load

**Tools**:
- NBomber.NET for load generation
- Custom ThreadSafetyTester pattern
- Fault injection framework

**Test Scenarios**:
```csharp
// Scenario 1: Timer Overlap Prevention
NBomberRunner
    .RegisterScenario(Scenario.Create("timer_overlap", async context =>
    {
        // Spawn 100 concurrent file discovery operations
        // Verify no overlapping ProcessPendingFiles executions
        // Measure memory usage and exception rates
    }))
    .Run();

// Scenario 2: Event Handler Safety
NBomberRunner
    .RegisterScenario(Scenario.Create("event_safety", async context =>
    {
        // Register 50 concurrent event subscribers
        // Generate file discovery events rapidly
        // Verify all events received exactly once
    }))
    .Run();

// Scenario 3: Disposal Under Load
NBomberRunner
    .RegisterScenario(Scenario.Create("disposal_safety", async context =>
    {
        // Start/stop service rapidly while processing files
        // Verify clean shutdown without deadlocks
        // Check for resource leaks
    }))
    .Run();
```

**Success Criteria**:
- 50-100 concurrent operations without failures
- Zero memory leaks during 1-hour stress test
- Clean shutdown under all load conditions
- No exceptions in timer callbacks

### Phase 5.1B: Multi-Process Docker Validation (Days 3-4)

**Objective**: Validate fixes under real process boundaries and resource contention

**Architecture**:
```yaml
# docker-compose.yml
version: '3.8'
services:
  forker-instance-1:
    build: .
    volumes:
      - shared-source:/app/source
      - target-1:/app/target

  forker-instance-2:
    build: .
    volumes:
      - shared-source:/app/source
      - target-2:/app/target

  file-generator:
    build: ./test-tools
    volumes:
      - shared-source:/app/source
    command: generate-medical-files --count=1000 --size=500MB-2GB
```

**Test Scenarios**:
- **Concurrent File Discovery**: Multiple ForkerDotNet instances monitoring same source
- **Resource Contention**: File system I/O pressure with realistic medical file sizes
- **Process Crash Recovery**: Kill processes during file processing, verify recovery
- **Network Partition Simulation**: Simulate hospital network conditions

**Success Criteria**:
- Multiple processes handle same files without conflicts
- Graceful handling of process termination during file processing
- No duplicate or lost file processing across instances
- Clean state recovery after crashes

### Phase 5.1C: Hyper-V VM Production Simulation (Day 5)

**Objective**: Final validation under realistic production conditions

**Infrastructure**:
- **VM Setup**: 3 Ubuntu VMs (2 ForkerDotNet instances, 1 file server)
- **Network**: Shared SMB/NFS file system simulating hospital environment
- **Load**: Realistic medical imaging file patterns (SVS, TIFF, NDPI, SCN)
- **Monitoring**: Performance counters, memory usage, file integrity

**Test Scenarios**:
- **Hospital Workflow Simulation**: 500MB-20GB files appearing throughout day
- **Network Latency**: Simulate WAN conditions between sites
- **Storage Pressure**: Fill source filesystem to 95% capacity
- **Concurrent Users**: Multiple radiologists accessing files during processing

**Success Criteria**:
- 24-hour continuous operation without failures
- Processing 100+ large medical files without data loss
- Network interruption recovery
- Memory usage stable under load

## Technical Implementation Details

### Race Condition Fixes

#### 1. Timer Callback Fix
```csharp
// Before (Race Condition):
private async void ProcessPendingFiles(object? state)

// After (Thread Safe):
private readonly SemaphoreSlim _processingLock = new(1, 1);
private volatile bool _processingInProgress;

private void ProcessPendingFilesCallback(object? state)
{
    if (_processingInProgress) return;

    _ = Task.Run(async () =>
    {
        await _processingLock.WaitAsync();
        try
        {
            _processingInProgress = true;
            await ProcessPendingFilesAsync();
        }
        finally
        {
            _processingInProgress = false;
            _processingLock.Release();
        }
    });
}
```

#### 2. Thread-Safe Event Handling
```csharp
// Before (Race Condition):
FileDiscovered?.Invoke(this, eventArgs);

// After (Thread Safe):
private readonly ConcurrentBag<EventHandler<FileDiscoveredEventArgs>> _eventHandlers = new();

public event EventHandler<FileDiscoveredEventArgs>? FileDiscovered
{
    add => _eventHandlers.Add(value);
    remove => { /* Implement safe removal */ }
}

private void NotifyFileDiscovered(FileDiscoveredEventArgs eventArgs)
{
    var handlers = _eventHandlers.ToArray();
    Parallel.ForEach(handlers, handler =>
    {
        try
        {
            handler?.Invoke(this, eventArgs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Event handler failed");
        }
    });
}
```

#### 3. Async Disposal Pattern
```csharp
// Before (Deadlock Risk):
public void Dispose()
{
    StopAsync().GetAwaiter().GetResult();
}

// After (Safe Async Disposal):
public async ValueTask DisposeAsync()
{
    await StopAsync(_cancellationTokenSource.Token);
    _cancellationTokenSource.Dispose();
    _processingLock.Dispose();
    _fileWatcher?.Dispose();
}

public void Dispose()
{
    DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(30));
}
```

### Monitoring and Metrics

**Key Performance Indicators**:
- Timer callback execution count and duration
- Event handler execution time and failure rate
- Memory usage patterns during concurrent operations
- File processing throughput under load
- Service startup/shutdown time

**Alerting Thresholds**:
- Memory usage > 500MB (medical imaging workload)
- Event handler failures > 1%
- Timer callback overlaps > 0
- File processing delays > 5 minutes

## Risk Assessment

### High Risk Scenarios
1. **Production Deployment**: Race conditions in medical imaging could cause patient data loss
2. **Peak Load**: Hospital peak hours with 50+ concurrent large file transfers
3. **Network Issues**: Intermittent connectivity causing partial file states
4. **System Maintenance**: Service restarts during active file processing

### Mitigation Strategies
1. **Comprehensive Testing**: Multi-phase validation before production deployment
2. **Gradual Rollout**: Deploy to test environment first, then staging, then production
3. **Monitoring**: Real-time detection of race conditions and performance degradation
4. **Rollback Plan**: Immediate rollback capability if issues detected

## Success Criteria & Exit Conditions

### Technical Criteria
- ✅ All 4 identified race conditions eliminated
- ✅ NBomber stress tests pass with 50-100 concurrent operations
- ✅ Multi-process Docker validation successful
- ✅ 24-hour VM production simulation without failures
- ✅ Memory usage stable under sustained load
- ✅ Zero data loss or corruption in test scenarios

### Business Criteria
- ✅ Medical imaging file processing reliability > 99.99%
- ✅ Service availability during maintenance operations
- ✅ Compliance with NHS-grade reliability requirements
- ✅ Production readiness certification from technical team

## Timeline and Resources

**Phase 5.1A** (Days 1-2): NBomber in-process testing
**Phase 5.1B** (Days 3-4): Docker multi-process validation
**Phase 5.1C** (Day 5): Hyper-V VM production simulation

**Total Duration**: 5 days
**Resources Required**:
- Development environment with NBomber.NET
- Docker Desktop for multi-process testing
- Hyper-V access for VM simulation
- Test data: Realistic medical imaging files (500MB-20GB)

## Conclusion

This comprehensive testing strategy ensures that race conditions in FileDiscoveryService are completely eliminated before production deployment. The hybrid approach provides early feedback, realistic validation, and production confidence for medical imaging workflows where data integrity is paramount.

The phased approach allows for iterative improvement and early detection of issues, significantly reducing the risk of production failures that could impact patient care.
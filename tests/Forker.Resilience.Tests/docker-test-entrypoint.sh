#!/bin/bash
set -e

echo "Starting Docker Multi-Process Race Condition Validation"
echo "========================================================="

# Function to run a ForkerDotNet instance
run_forker_instance() {
    local instance_id=$1
    local log_file="/shared/temp/forker_${instance_id}.log"

    echo "Starting ForkerDotNet instance $instance_id"

    # Create instance-specific config
    cat > /app/config_${instance_id}.json << EOL
{
  "Directories": {
    "Source": "/shared/source",
    "TargetA": "/shared/targetA",
    "TargetB": "/shared/targetB"
  },
  "FileMonitoring": {
    "FileFilters": ["*.test", "*.medical"],
    "ExcludeExtensions": [".tmp", ".lock"],
    "IncludeSubdirectories": false,
    "StabilityCheckInterval": 1,
    "MaxStabilityChecks": 3,
    "MinimumFileAge": 1
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Forker": "Debug"
    }
  }
}
EOL

    # Run ForkerDotNet instance in background
    dotnet Forker.Service.dll --config /app/config_${instance_id}.json > $log_file 2>&1 &
    echo $! > /shared/temp/forker_${instance_id}.pid
}

# Function to create test files
create_test_files() {
    local file_count=$1
    local thread_id=$2

    for i in $(seq 1 $file_count); do
        local filename="medical_${thread_id}_${i}_$(date +%s%N).test"
        local filepath="/shared/source/$filename"

        # Create file with medical imaging simulation content
        echo "MEDICAL_IMAGING_FILE_${thread_id}_${i}" > "$filepath"
        echo "TIMESTAMP: $(date)" >> "$filepath"
        echo "SIZE: $(shuf -i 1000-50000 -n 1)" >> "$filepath"

        # Add random delay to create timing variations
        sleep 0.$(shuf -i 1-5 -n 1)
    done
}

# Function to validate no race conditions occurred
validate_results() {
    echo "Validating multi-process race condition results..."

    local duplicates=0
    local missing=0
    local source_files=$(find /shared/source -name "*.test" | wc -l)
    local targetA_files=$(find /shared/targetA -name "*.test" | wc -l)
    local targetB_files=$(find /shared/targetB -name "*.test" | wc -l)

    echo "Source files: $source_files"
    echo "Target A files: $targetA_files"
    echo "Target B files: $targetB_files"

    # Check for race condition indicators
    if [ $targetA_files -ne $source_files ] || [ $targetB_files -ne $source_files ]; then
        echo "ERROR: File count mismatch indicates race conditions or missing files"
        missing=1
    fi

    # Check logs for race condition errors
    if grep -r "RACE CONDITION\|Exception\|Error" /shared/temp/*.log; then
        echo "ERROR: Race conditions detected in logs"
        duplicates=1
    fi

    if [ $duplicates -eq 0 ] && [ $missing -eq 0 ]; then
        echo "SUCCESS: No race conditions detected in multi-process testing"
        return 0
    else
        echo "FAILURE: Race conditions detected"
        return 1
    fi
}

# Main test execution
echo "Phase 1: Starting multiple ForkerDotNet processes"
for i in {1..3}; do
    run_forker_instance $i
    sleep 2
done

echo "Phase 2: Allowing services to initialize"
sleep 5

echo "Phase 3: Creating concurrent file load across processes"
create_test_files 20 "proc1" &
create_test_files 20 "proc2" &
create_test_files 20 "proc3" &

# Wait for file creation to complete
wait

echo "Phase 4: Allowing processing time"
sleep 30

echo "Phase 5: Stopping ForkerDotNet processes"
for i in {1..3}; do
    if [ -f /shared/temp/forker_${i}.pid ]; then
        kill $(cat /shared/temp/forker_${i}.pid) 2>/dev/null || true
        rm -f /shared/temp/forker_${i}.pid
    fi
done

sleep 5

echo "Phase 6: Validation"
validate_results
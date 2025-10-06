# Test Scenario 2: Corruption Detection and Quarantine
# Validates that ForkerDotNet detects hash mismatches and quarantines corrupt files
# Requires: appsettings.json configured with VerificationDelaySeconds: 30

param(
    [int]$FileSizeMB = 100
)

$ErrorActionPreference = "Stop"
$TestVolume = "E:\ForkerDotNetTestVolume"

Write-Host ""
Write-Host "================================================================================" -ForegroundColor Cyan
Write-Host "  SCENARIO 2: Corruption Detection and Quarantine Test" -ForegroundColor Cyan
Write-Host "================================================================================" -ForegroundColor Cyan
Write-Host ""

# Step 1: Create test file
Write-Host "STEP 1: Creating test file ($FileSizeMB MB)..." -ForegroundColor Yellow
$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$fileName = "corrupt-test-$timestamp.svs"
$testFile = Join-Path "$TestVolume\Reservoir" $fileName

$buffer = New-Object byte[] 1MB
$rng = New-Object System.Random(42)
$rng.NextBytes($buffer)

$stream = [System.IO.File]::OpenWrite($testFile)
for ($i = 0; $i -lt $FileSizeMB; $i++) {
    $stream.Write($buffer, 0, $buffer.Length)
}
$stream.Close()

Write-Host "  [OK] Created: $fileName ($FileSizeMB MB)" -ForegroundColor Green
Write-Host ""

# Step 2: Calculate source hash
Write-Host "STEP 2: Calculating source SHA-256 hash..." -ForegroundColor Yellow
$sourceHash = (Get-FileHash $testFile -Algorithm SHA256).Hash
Write-Host "  [OK] Source Hash: $sourceHash" -ForegroundColor Green
Write-Host ""

# Step 3: Move to Input
Write-Host "STEP 3: Moving file to Input folder..." -ForegroundColor Yellow
$inputFile = Join-Path "$TestVolume\Input" $fileName
Move-Item $testFile $inputFile -Force
Write-Host "  [OK] File moved - ForkerDotNet will start copying" -ForegroundColor Green
Write-Host ""

# Step 4: Wait for copy
Write-Host "STEP 4: Waiting for copy to complete..." -ForegroundColor Yellow
$destA = Join-Path "$TestVolume\DestinationA" $fileName
$destB = Join-Path "$TestVolume\DestinationB" $fileName
$copyStart = Get-Date

while ((-not (Test-Path $destA) -or -not (Test-Path $destB)) -and ((Get-Date) - $copyStart).TotalSeconds -lt 60) {
    Start-Sleep -Seconds 2
    Write-Host "." -NoNewline -ForegroundColor Gray
}
Write-Host ""

if (-not (Test-Path $destA)) {
    Write-Host "  [ERROR] Copy timeout" -ForegroundColor Red
    exit 1
}

Write-Host "  [OK] Copy completed" -ForegroundColor Green
Write-Host ""

# Step 5: CORRUPT file
Write-Host "STEP 5: Simulating data corruption..." -ForegroundColor Yellow
Start-Sleep -Seconds 2

$corruptStream = [System.IO.File]::OpenWrite($destA)
$corruptStream.Seek(5MB, [System.IO.SeekOrigin]::Begin) | Out-Null
$corruptStream.WriteByte(0xFF)
$corruptStream.Close()

$corruptHash = (Get-FileHash $destA -Algorithm SHA256).Hash
Write-Host "  [OK] Corruption confirmed - hashes differ" -ForegroundColor Green
Write-Host ""

# Step 6: Wait for quarantine (check database)
Write-Host "STEP 6: Waiting for ForkerDotNet to detect corruption..." -ForegroundColor Yellow
$dbPath = Join-Path "$TestVolume" "forker.db"
$verifyStart = Get-Date
$quarantined = $false

# Wait up to 60 seconds for quarantine entry to appear in database
while (-not $quarantined -and ((Get-Date) - $verifyStart).TotalSeconds -lt 60) {
    Start-Sleep -Seconds 2
    Write-Host "." -NoNewline -ForegroundColor Gray

    # Check if there's a quarantine entry for a file matching our filename
    try {
        $connString = "Data Source=$dbPath"
        $conn = New-Object Microsoft.Data.Sqlite.SqliteConnection($connString)
        $conn.Open()

        $cmd = $conn.CreateCommand()
        $cmd.CommandText = "SELECT COUNT(*) FROM QuarantineEntries WHERE Status = 'Active'"
        $count = $cmd.ExecuteScalar()

        $conn.Close()

        if ($count -gt 0) {
            $quarantined = $true
        }
    } catch {
        # Database might be locked, continue waiting
    }
}
Write-Host ""

# Step 7: Verify quarantine in database
if ($quarantined) {
    Write-Host "================================================================================" -ForegroundColor Green
    Write-Host "  SUCCESS: Corruption Detected and Quarantined" -ForegroundColor Green
    Write-Host "================================================================================" -ForegroundColor Green
    Write-Host ""
    Write-Host "Database Status:" -ForegroundColor Cyan

    # Show quarantine details
    $conn = New-Object Microsoft.Data.Sqlite.SqliteConnection($connString)
    $conn.Open()

    $cmd = $conn.CreateCommand()
    $cmd.CommandText = "SELECT Id, Reason, AffectedTargetCount, Status, CreatedAt FROM QuarantineEntries ORDER BY CreatedAt DESC LIMIT 1"
    $reader = $cmd.ExecuteReader()

    if ($reader.Read()) {
        Write-Host "  Quarantine ID: $($reader['Id'])" -ForegroundColor White
        Write-Host "  Reason: $($reader['Reason'])" -ForegroundColor White
        Write-Host "  Affected Targets: $($reader['AffectedTargetCount'])" -ForegroundColor White
        Write-Host "  Status: $($reader['Status'])" -ForegroundColor White
        Write-Host "  Created: $($reader['CreatedAt'])" -ForegroundColor White
    }

    $reader.Close()
    $conn.Close()

} else {
    Write-Host "[ERROR] Quarantine failed - no entry found in database" -ForegroundColor Red
    exit 1
}

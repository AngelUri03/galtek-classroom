# Only this kit script changes the installed Service. It copies exactly two binaries.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Run PowerShell as Administrator.' }
$serviceName = 'GaltekClassroomAgent'
$sourceDir = 'G:\GaltekClassroom\20E.0B2\agent-service'
$agentDir = 'C:\Program Files\Galtek\Classroom\Agent'
$backupRoot = 'C:\Program Files\Galtek\Classroom\Agent-backups\20E.0B2'
$expected = @{
    'GaltekClassroom.Agent.Service.exe' = 'BF0CA57211DFB8FF8742EB82C466607B911E41B1578BE995ECF35678DD753D69'
    'GaltekClassroom.Agent.Service.dll' = '8967DBCDE8E9EC41DAFB771C6FC9FDAEE7D10745F0096F5AFF7BD6F5DAC8A7C0'
}

function Test-AgentBinaryReleased {
    param([Parameter(Mandatory)][string]$Path)

    try {
        $stream = [System.IO.File]::Open(
            $Path,
            [System.IO.FileMode]::Open,
            [System.IO.FileAccess]::Read,
            [System.IO.FileShare]::None)
        $stream.Dispose()
        return $true
    }
    catch [System.IO.IOException] {
        return $false
    }
}

function Wait-AgentServiceProcessReleased {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string[]]$BinaryPaths,
        [Parameter(Mandatory)][TimeSpan]$Timeout
    )

    $timer = [System.Diagnostics.Stopwatch]::StartNew()
    do {
        $serviceState = Get-CimInstance Win32_Service -Filter "Name='$Name'"
        $allBinariesReleased = $true
        foreach ($binaryPath in $BinaryPaths) {
            if (-not (Test-AgentBinaryReleased -Path $binaryPath)) {
                $allBinariesReleased = $false
                break
            }
        }

        if ($null -ne $serviceState `
            -and $serviceState.State -eq 'Stopped' `
            -and [uint32]$serviceState.ProcessId -eq 0 `
            -and $allBinariesReleased) {
            return
        }

        Start-Sleep -Milliseconds 200
    } while ($timer.Elapsed -lt $Timeout)

    $pidValue = if ($null -eq $serviceState) { '<missing>' } else { $serviceState.ProcessId }
    $stateValue = if ($null -eq $serviceState) { '<missing>' } else { $serviceState.State }
    throw "Service process did not release within $($Timeout.TotalSeconds)s (State=$stateValue PID=$pidValue). Service remains stopped; inspect before deployment."
}

$configPath = Join-Path $agentDir 'appsettings.json'
$service = Get-CimInstance Win32_Service -Filter "Name='$serviceName'"
if ($null -eq $service -or $service.StartMode -ne 'Auto' -or $service.StartName -notin @('LocalSystem','Local System')) { throw 'Service missing or configuration differs from Automatic/LocalSystem. No deployment.' }
if (-not (Test-Path -LiteralPath $configPath -PathType Leaf)) { throw 'appsettings.json missing. No deployment.' }
foreach ($name in $expected.Keys) {
    $source = Join-Path $sourceDir $name
    $destination = Join-Path $agentDir $name
    if (-not (Test-Path -LiteralPath $source -PathType Leaf) -or -not (Test-Path -LiteralPath $destination -PathType Leaf)) { throw "$name missing at source or destination. No deployment." }
    if ((Get-FileHash -Algorithm SHA256 -LiteralPath $source).Hash -ne $expected[$name]) { throw "$name USB hash mismatch. No deployment." }
}
$configHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $configPath).Hash
$backup = Join-Path $backupRoot (Get-Date -Format 'yyyyMMdd-HHmmss-fff')
New-Item -ItemType Directory -Path $backup -Force | Out-Null
Copy-Item -LiteralPath $configPath -Destination (Join-Path $backup 'appsettings.json') -ErrorAction Stop
foreach ($name in $expected.Keys) { Copy-Item -LiteralPath (Join-Path $agentDir $name) -Destination (Join-Path $backup $name) -ErrorAction Stop }
if ((Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $backup 'appsettings.json')).Hash -ne $configHash) { throw 'Backup configuration hash mismatch. No deployment.' }
"Backup: $backup"
"appsettings.json SHA256 before: $configHash"
Stop-Service -Name $serviceName -ErrorAction Stop
(Get-Service -Name $serviceName).WaitForStatus('Stopped', [TimeSpan]::FromSeconds(45))
$installedBinaryPaths = @($expected.Keys | ForEach-Object { Join-Path $agentDir $_ })
Wait-AgentServiceProcessReleased `
    -Name $serviceName `
    -BinaryPaths $installedBinaryPaths `
    -Timeout ([TimeSpan]::FromSeconds(45))
foreach ($name in $expected.Keys) { Copy-Item -LiteralPath (Join-Path $sourceDir $name) -Destination (Join-Path $agentDir $name) -Force -ErrorAction Stop }
foreach ($name in $expected.Keys) {
    if ((Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $agentDir $name)).Hash -ne $expected[$name]) { throw "$name installed hash mismatch. Service remains stopped; use backup for recovery." }
}
if ((Get-FileHash -Algorithm SHA256 -LiteralPath $configPath).Hash -ne $configHash) { throw 'FAIL CLOSED: appsettings.json changed. Service remains stopped; inspect before recovery.' }
Start-Service -Name $serviceName -ErrorAction Stop
(Get-Service -Name $serviceName).WaitForStatus('Running', [TimeSpan]::FromSeconds(45))
$after = Get-CimInstance Win32_Service -Filter "Name='$serviceName'"
if ($after.State -ne 'Running' -or $after.StartMode -ne 'Auto' -or $after.StartName -notin @('LocalSystem','Local System')) { throw 'FAIL CLOSED: service state/configuration verification failed.' }
if ((Get-FileHash -Algorithm SHA256 -LiteralPath $configPath).Hash -ne $configHash) { Stop-Service -Name $serviceName -ErrorAction Stop; throw 'FAIL CLOSED: appsettings.json changed after start. Service stopped.' }
"PASS: Running, Automatic, LocalSystem; appsettings.json unchanged ($configHash)."
foreach ($name in $expected.Keys) { "$name SHA256=$((Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $agentDir $name)).Hash)" }

# LAB ONLY. Collects the already-armed F2 evidence for the current boot.
# It never starts, stops, restarts or kills the Galtek service.
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$serviceName = 'GaltekClassroomAgent'
$diagnosticRoot = 'C:\ProgramData\Galtek\Classroom\Diagnostics\20E.0B2-F2'
$tracePath = Join-Path $diagnosticRoot 'corehost-trace.log'
$registryPath = "Registry::HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\$serviceName"
$backupPath = Join-Path $registryPath 'Galtek20E0B2F2DiagnosticsBackup'
$boot = (Get-CimInstance Win32_OperatingSystem).LastBootUpTime
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$collectionRoot = Join-Path $diagnosticRoot "collection-$stamp"
$collectionIssues = [Collections.Generic.List[string]]::new()

function Assert-Administrator {
    $principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'Run Windows PowerShell as Administrator.'
    }
}

function Write-EventSelection {
    param(
        [Parameter(Mandatory)][string] $Label,
        [Parameter(Mandatory)][string] $LogName,
        [Parameter(Mandatory)][scriptblock] $Predicate,
        [Parameter(Mandatory)][string] $OutputPath,
        [int[]] $Ids
    )

    $log = Get-WinEvent -ListLog $LogName -ErrorAction SilentlyContinue
    if ($null -eq $log) {
        @("Label=$Label", "LogName=$LogName", 'Status=CHANNEL_NOT_PRESENT', 'Absence is not proof that no relevant event occurred.') |
            Set-Content -LiteralPath $OutputPath -Encoding UTF8
        return
    }
    if (-not $log.IsEnabled) {
        @("Label=$Label", "LogName=$LogName", 'Status=CHANNEL_DISABLED', 'The collector did not enable it. Absence is not proof that no relevant event occurred.') |
            Set-Content -LiteralPath $OutputPath -Encoding UTF8
        return
    }

    $filter = @{ LogName = $LogName; StartTime = $boot }
    if ($null -ne $Ids -and $Ids.Count -gt 0) { $filter.Id = $Ids }
    $events = @(Get-WinEvent -FilterHashtable $filter -ErrorAction SilentlyContinue | Where-Object $Predicate)
    $header = @(
        "Label=$Label"
        "LogName=$LogName"
        "Boot=$($boot.ToString('o'))"
        "MatchedCount=$($events.Count)"
        'Absence is not proof that no relevant event occurred.'
        ''
    )
    $body = foreach ($event in $events) {
        $message = if ([string]::IsNullOrWhiteSpace($event.Message)) { '<no rendered message>' } else { $event.Message.Trim() }
        "[$($event.TimeCreated.ToString('o'))] Id=$($event.Id) Provider=$($event.ProviderName) Level=$($event.LevelDisplayName)"
        $message
        ''
    }
    @($header + $body) | Set-Content -LiteralPath $OutputPath -Encoding UTF8
}

Assert-Administrator
if (-not (Test-Path -LiteralPath $backupPath)) { throw 'F2 backup state is missing. Run 06 before the cold boot.' }
New-Item -ItemType Directory -Path $collectionRoot -Force | Out-Null

$service = Get-CimInstance Win32_Service -Filter "Name='$serviceName'" -ErrorAction SilentlyContinue
$process = @(Get-CimInstance Win32_Process -Filter "Name='GaltekClassroom.Agent.Service.exe'" -ErrorAction SilentlyContinue)
@(
    "CollectedAt=$((Get-Date).ToString('o'))"
    "LastBootUpTime=$($boot.ToString('o'))"
    "ServiceState=$(if ($null -eq $service) { 'MISSING' } else { $service.State })"
    "ServiceProcessId=$(if ($null -eq $service) { '<unavailable>' } else { $service.ProcessId })"
    "MatchingProcessCount=$($process.Count)"
    'Collector did not call Start-Service, Stop-Service, Restart-Service or Stop-Process.'
) | Set-Content -LiteralPath (Join-Path $collectionRoot 'boot-summary.txt') -Encoding UTF8

$backupKey = Get-Item -LiteralPath $backupPath
$wprArmed = [int]$backupKey.GetValue('WprArmedByKit', 0)
$wpr = Get-Command wpr.exe -ErrorAction SilentlyContinue
if ($wprArmed -eq 1 -and $null -ne $wpr) {
    $etlPath = Join-Path $collectionRoot 'early-boot.etl'
    & $wpr.Source -stopboot $etlPath 'GaltekClassroomAgent 20E.0B2-F2 external early-boot diagnostic' *> (Join-Path $collectionRoot 'wpr-stop.txt')
    if ($LASTEXITCODE -ne 0) {
        $collectionIssues.Add("WPR stopboot failed with exit code $LASTEXITCODE; 08 must cancel the pending boot trace.")
    }
    else {
        $backupKey.SetValue('WprStoppedByCollector', 1, [Microsoft.Win32.RegistryValueKind]::DWord)
    }
}
else {
    @(
        'WPR boot trace was not collected.'
        "WprArmedByKit=$wprArmed"
        "WprExeAvailable=$($null -ne $wpr)"
        'Host trace and Windows event logs remain available.'
    ) | Set-Content -LiteralPath (Join-Path $collectionRoot 'wpr-not-collected.txt') -Encoding UTF8
}

if (Test-Path -LiteralPath $tracePath -PathType Leaf) {
    try {
        Copy-Item -LiteralPath $tracePath -Destination (Join-Path $collectionRoot 'corehost-trace.log') -ErrorAction Stop
    }
    catch {
        $collectionIssues.Add("corehost trace copy failed; original remains at ${tracePath}: $($_.Exception.Message)")
    }
    Get-Item -LiteralPath $tracePath |
        Select-Object FullName, Length, CreationTime, LastWriteTime |
        Format-List | Out-String | Set-Content -LiteralPath (Join-Path $collectionRoot 'corehost-trace-metadata.txt') -Encoding UTF8
}
else {
    'MISSING. This absence alone does not prove that the process or managed entrypoint did not execute.' |
        Set-Content -LiteralPath (Join-Path $collectionRoot 'corehost-trace-metadata.txt') -Encoding UTF8
}

$agentPattern = 'GaltekClassroomAgent|GaltekClassroom\.Agent\.Service|C:\\Program Files\\Galtek\\Classroom\\Agent'
Write-EventSelection -Label 'Service Control Manager' -LogName 'System' -Ids @(7000,7009,7011,7022,7023,7024,7031,7034,7036) `
    -Predicate { $_.Message -match [regex]::Escape($serviceName) } -OutputPath (Join-Path $collectionRoot 'events-system-scm.txt')
Write-EventSelection -Label 'Application runtime/crash/Agent' -LogName 'Application' `
    -Predicate { $_.ProviderName -in @('Application Error', 'Windows Error Reporting', '.NET Runtime', 'GaltekClassroom.Agent.Service', 'GaltekClassroomAgent', 'Galtek Classroom Agent Service') -and ($_.Message -match $agentPattern -or $_.ProviderName -match 'Galtek') } `
    -OutputPath (Join-Path $collectionRoot 'events-application.txt')
Write-EventSelection -Label 'Code Integrity' -LogName 'Microsoft-Windows-CodeIntegrity/Operational' `
    -Predicate { $_.Message -match $agentPattern } -OutputPath (Join-Path $collectionRoot 'events-code-integrity.txt')
Write-EventSelection -Label 'AppLocker EXE and DLL' -LogName 'Microsoft-Windows-AppLocker/EXE and DLL' `
    -Predicate { $_.Message -match $agentPattern } -OutputPath (Join-Path $collectionRoot 'events-applocker-exe-dll.txt')
Write-EventSelection -Label 'Microsoft Defender' -LogName 'Microsoft-Windows-Windows Defender/Operational' `
    -Predicate { $_.Message -match $agentPattern } -OutputPath (Join-Path $collectionRoot 'events-defender.txt')

$securityLog = Get-WinEvent -ListLog Security -ErrorAction SilentlyContinue
if ($null -ne $securityLog -and $securityLog.IsEnabled) {
    $processEvents = @(Get-WinEvent -FilterHashtable @{ LogName='Security'; Id=4688; StartTime=$boot } -ErrorAction SilentlyContinue |
        Where-Object { $_.Message -match $agentPattern })
    if ($processEvents.Count -gt 0) {
        $processEvents | ForEach-Object {
            "[$($_.TimeCreated.ToString('o'))] Id=4688 Provider=$($_.ProviderName)"
            $_.Message
            ''
        } | Set-Content -LiteralPath (Join-Path $collectionRoot 'events-security-4688-agent.txt') -Encoding UTF8
    }
    else {
        'No matching 4688 event existed since this boot. The collector did not enable process-creation auditing.' |
            Set-Content -LiteralPath (Join-Path $collectionRoot 'events-security-4688-agent.txt') -Encoding UTF8
    }
}
else {
    'Security log unavailable/disabled. The collector did not change audit policy.' |
        Set-Content -LiteralPath (Join-Path $collectionRoot 'events-security-4688-agent.txt') -Encoding UTF8
}

$bootstrapPath = 'C:\ProgramData\Galtek\Classroom\Diagnostics\startup-bootstrap.log'
if (Test-Path -LiteralPath $bootstrapPath -PathType Leaf) {
    Get-Content -LiteralPath $bootstrapPath |
        Where-Object {
            if ($_ -match '^utc=(?<Utc>\S+) ') {
                $timestamp = [DateTimeOffset]::MinValue
                [DateTimeOffset]::TryParse($Matches.Utc, [ref]$timestamp) -and $timestamp.LocalDateTime -ge $boot
            }
            else { $false }
        } | Set-Content -LiteralPath (Join-Path $collectionRoot 'bootstrap-current-boot.log') -Encoding UTF8
}
else {
    'MISSING' | Set-Content -LiteralPath (Join-Path $collectionRoot 'bootstrap-current-boot.log') -Encoding UTF8
}

if ($collectionIssues.Count -gt 0) {
    $collectionIssues | Set-Content -LiteralPath (Join-Path $collectionRoot 'collection-issues.txt') -Encoding UTF8
    throw "Collection completed with issues at $collectionRoot. Preserve it, do not run Start-Service, then run 08 to restore/cancel tracing."
}

"PASS: collection saved at $collectionRoot"
'Do not run Start-Service yet. Copy/preserve the collection, then run 08 to restore temporary tracing configuration.'

# LAB ONLY. Restores the exact prior per-service Environment value and cancels WPR boot tracing.
# It never starts, stops, restarts or kills the Galtek service.
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$serviceName = 'GaltekClassroomAgent'
$diagnosticRoot = 'C:\ProgramData\Galtek\Classroom\Diagnostics\20E.0B2-F2'
$registryPath = "Registry::HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\$serviceName"
$serviceKeyPath = "SYSTEM\CurrentControlSet\Services\$serviceName"
$backupPath = Join-Path $registryPath 'Galtek20E0B2F2DiagnosticsBackup'
$completionPath = Join-Path $diagnosticRoot 'restore-complete.txt'

. (Join-Path $PSScriptRoot 'early-boot-trace-registry-helpers.ps1')

$principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run Windows PowerShell as Administrator.'
}
if (-not (Test-Path -LiteralPath $backupPath)) {
    $readOnlyServiceKey = [Microsoft.Win32.Registry]::LocalMachine.OpenSubKey($serviceKeyPath, $false)
    if ($null -eq $readOnlyServiceKey) { throw "Service registry key is missing: HKLM\$serviceKeyPath" }
    try {
        $current = Get-ServiceEnvironmentValue -Key $readOnlyServiceKey
    }
    finally {
        $readOnlyServiceKey.Dispose()
    }

    if (Test-Path -LiteralPath $completionPath -PathType Leaf) {
        $completion = @{}
        foreach ($line in Get-Content -LiteralPath $completionPath) {
            $separator = $line.IndexOf('=')
            if ($separator -gt 0) { $completion[$line.Substring(0, $separator)] = $line.Substring($separator + 1) }
        }

        $previousWasAbsent = $completion['PreviousEnvironmentExisted'] -eq 'False'
        $previousWasPresent = $completion['PreviousEnvironmentExisted'] -eq 'True'
        $currentHash = if ($current.Exists) { Get-ServiceEnvironmentStateHash -Values $current.Values } else { $null }
        $presentStateMatches = $previousWasPresent `
            -and $current.Exists `
            -and $completion.ContainsKey('PreviousEnvironmentHash') `
            -and $currentHash -ceq $completion['PreviousEnvironmentHash']
        if (($previousWasAbsent -and -not $current.Exists) -or $presentStateMatches) {
            'PASS: prior per-service Environment already matches the completed restore state.'
            'WPR: unchanged by idempotent re-run.'
            return
        }
    }

    throw 'F2 backup state is missing; refusing to guess or modify the service Environment.'
}

$backupKey = Get-Item -LiteralPath $backupPath -ErrorAction Stop
$previousExisted = [int]$backupKey.GetValue('PreviousEnvironmentExisted', -1)
if ($previousExisted -notin @(0, 1)) { throw 'F2 backup state is invalid; no registry change was made.' }
$previousCoreHostEntries = @($backupKey.GetValue(
    'PreviousCoreHostEntries',
    [string[]]@(),
    [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames))
if (@($previousCoreHostEntries | Where-Object { $_ -notmatch '^COREHOST_(TRACE|TRACEFILE|TRACE_VERBOSITY)=' }).Count -gt 0) {
    throw 'F2 backup contains an unexpected environment name; no registry change was made.'
}
$previousEnvironment = if ($previousExisted -eq 1) {
    [string[]]$previousCoreHostEntries
}
else { @() }
$wprArmed = [int]$backupKey.GetValue('WprArmedByKit', 0)
$wprStoppedByCollector = [int]$backupKey.GetValue('WprStoppedByCollector', 0)

$serviceKey = [Microsoft.Win32.Registry]::LocalMachine.OpenSubKey($serviceKeyPath, $true)
if ($null -eq $serviceKey) { throw "Could not open HKLM\$serviceKeyPath with write access." }
try {
    Restore-ServiceEnvironmentValue `
        -WritableServiceKey $serviceKey `
        -PreviousExisted ([bool]$previousExisted) `
        -PreviousEnvironment $previousEnvironment
}
finally {
    $serviceKey.Dispose()
}

$wpr = Get-Command wpr.exe -ErrorAction SilentlyContinue
$wprResult = 'WPR_NOT_ARMED_BY_KIT'
if ($wprArmed -eq 1 -and $wprStoppedByCollector -eq 1) {
    $wprResult = 'WPR_ALREADY_STOPPED_BY_COLLECTOR'
}
elseif ($wprArmed -eq 1) {
    if ($null -eq $wpr) { throw 'WPR was armed by this kit but wpr.exe is now unavailable; boot trace was not cancelled.' }
    & $wpr.Source -cancelboot *> (Join-Path $diagnosticRoot 'wpr-cancel-or-already-stopped.txt')
    if ($LASTEXITCODE -ne 0) { throw "WPR cancelboot failed with exit code $LASTEXITCODE; backup state was retained for recovery." }
    $wprResult = 'WPR_CANCELBOOT_PASS'
}

$verifyKey = [Microsoft.Win32.Registry]::LocalMachine.OpenSubKey($serviceKeyPath, $false)
if ($null -eq $verifyKey) { throw "Service registry key is missing after restore: HKLM\$serviceKeyPath" }
try {
    $effectiveState = Get-ServiceEnvironmentValue -Key $verifyKey
    if ($effectiveState.Exists -ne [bool]$previousExisted) { throw 'Service Environment existence did not restore correctly.' }
    if ($previousExisted -eq 1) {
        if ([string]::Join([char]0, $effectiveState.Values) -cne [string]::Join([char]0, $previousEnvironment)) {
            throw 'Service Environment content did not restore exactly.'
        }
    }
}
finally {
    $verifyKey.Dispose()
}

@(
    "RestoredAt=$((Get-Date).ToString('o'))"
    "PreviousEnvironmentExisted=$([bool]$previousExisted)"
    "PreviousEnvironmentHash=$(Get-ServiceEnvironmentStateHash -Values $previousEnvironment)"
    "WprResult=$wprResult"
    'No environment values were printed.'
) | Set-Content -LiteralPath $completionPath -Encoding UTF8
Remove-Item -LiteralPath $backupPath -Recurse -Force

$service = Get-CimInstance Win32_Service -Filter "Name='$serviceName'" -ErrorAction SilentlyContinue
'PASS: prior per-service environment restored exactly; F2 registry backup removed.'
"WPR: $wprResult"
"Service state was not changed: $(if ($null -eq $service) { 'MISSING' } else { $service.State })"
'If the service is stopped, a later manual Start-Service is now allowed for same-boot isolation.'

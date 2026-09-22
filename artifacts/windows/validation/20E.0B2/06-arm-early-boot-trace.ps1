# LAB ONLY. Arms per-service .NET host tracing and, when available, one WPR boot trace.
# Does not stop/start/restart the service or change its SCM identity/start/image/dependencies.
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$serviceName = 'GaltekClassroomAgent'
$diagnosticRoot = 'C:\ProgramData\Galtek\Classroom\Diagnostics\20E.0B2-F2'
$tracePath = Join-Path $diagnosticRoot 'corehost-trace.log'
$wprTempPath = Join-Path $diagnosticRoot 'wpr-temp'
$registryPath = "Registry::HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\$serviceName"
$backupPath = Join-Path $registryPath 'Galtek20E0B2F2DiagnosticsBackup'
$profilePath = Join-Path $PSScriptRoot '20E.0B2-F2.wprp'

function Assert-Administrator {
    $principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'Run Windows PowerShell as Administrator.'
    }
}

function Set-DiagnosticDirectoryAcl {
    param([Parameter(Mandatory)][string] $Path)

    $inheritance = [Security.AccessControl.InheritanceFlags]'ContainerInherit, ObjectInherit'
    $propagation = [Security.AccessControl.PropagationFlags]::None
    $allow = [Security.AccessControl.AccessControlType]::Allow
    $acl = [Security.AccessControl.DirectorySecurity]::new()
    $acl.SetAccessRuleProtection($true, $false)
    foreach ($sidValue in @('S-1-5-18', 'S-1-5-32-544')) {
        $sid = [Security.Principal.SecurityIdentifier]::new($sidValue)
        $rule = [Security.AccessControl.FileSystemAccessRule]::new(
            $sid,
            [Security.AccessControl.FileSystemRights]::FullControl,
            $inheritance,
            $propagation,
            $allow)
        [void] $acl.AddAccessRule($rule)
    }
    Set-Acl -LiteralPath $Path -AclObject $acl
}

function Get-ServiceEnvironmentValue {
    param([Parameter(Mandatory)][Microsoft.Win32.RegistryKey] $Key)

    if ($Key.GetValueNames() -notcontains 'Environment') {
        return [pscustomobject]@{ Exists = $false; Kind = $null; Values = @() }
    }

    $kind = $Key.GetValueKind('Environment')
    if ($kind -ne [Microsoft.Win32.RegistryValueKind]::MultiString) {
        throw "The existing service Environment value is $kind, not REG_MULTI_SZ. No change was made."
    }

    $values = @($Key.GetValue(
        'Environment',
        $null,
        [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames))
    return [pscustomobject]@{ Exists = $true; Kind = $kind; Values = $values }
}

Assert-Administrator

$service = Get-CimInstance Win32_Service -Filter "Name='$serviceName'" -ErrorAction Stop
if ($null -eq $service) { throw "Service $serviceName is missing." }
if ($service.StartMode -ne 'Auto' -or $service.StartName -notin @('LocalSystem', 'Local System')) {
    throw 'Service configuration differs from Automatic/LocalSystem. No trace was armed.'
}
if (-not (Test-Path -LiteralPath $profilePath -PathType Leaf)) {
    throw "WPR profile is missing: $profilePath"
}
if (Test-Path -LiteralPath $backupPath) {
    throw 'F2 already appears armed. Run 08-restore-early-boot-trace.ps1 before arming again.'
}

New-Item -ItemType Directory -Path $diagnosticRoot -Force | Out-Null
New-Item -ItemType Directory -Path $wprTempPath -Force | Out-Null
Set-DiagnosticDirectoryAcl -Path $diagnosticRoot

$serviceKey = Get-Item -LiteralPath $registryPath -ErrorAction Stop
$previous = Get-ServiceEnvironmentValue -Key $serviceKey
$reservedNames = @('COREHOST_TRACE', 'COREHOST_TRACEFILE', 'COREHOST_TRACE_VERBOSITY')
$previousCoreHostEntries = [Collections.Generic.List[string]]::new()
$unexpectedEntries = [Collections.Generic.List[string]]::new()
foreach ($entry in $previous.Values) {
    $separator = $entry.IndexOf('=')
    $name = if ($separator -gt 0) { $entry.Substring(0, $separator) } else { '' }
    if ($reservedNames -contains $name) { $previousCoreHostEntries.Add($entry) }
    else { $unexpectedEntries.Add($entry) }
}
if ($unexpectedEntries.Count -gt 0) {
    throw 'The service already has non-COREHOST Environment entries. No values were printed or copied; no trace was armed.'
}

if (Test-Path -LiteralPath $tracePath -PathType Leaf) {
    $archivePath = Join-Path $diagnosticRoot ("corehost-trace-before-arm-{0}.log" -f (Get-Date -Format 'yyyyMMdd-HHmmss'))
    Move-Item -LiteralPath $tracePath -Destination $archivePath -ErrorAction Stop
}

$backupKey = New-Item -Path $backupPath -Force
$backupKey.SetValue('PreviousEnvironmentExisted', [int]$previous.Exists, [Microsoft.Win32.RegistryValueKind]::DWord)
$backupKey.SetValue('PreviousCoreHostEntries', $previousCoreHostEntries.ToArray(), [Microsoft.Win32.RegistryValueKind]::MultiString)
$backupKey.SetValue('ArmedAtUtc', [DateTimeOffset]::UtcNow.ToString('O'), [Microsoft.Win32.RegistryValueKind]::String)

$environment = [Collections.Generic.List[string]]::new()
foreach ($entry in $previous.Values) {
    $separator = $entry.IndexOf('=')
    $name = if ($separator -gt 0) { $entry.Substring(0, $separator) } else { '' }
    if ($reservedNames -notcontains $name) { $environment.Add($entry) }
}
$environment.Add('COREHOST_TRACE=1')
$environment.Add("COREHOST_TRACEFILE=$tracePath")
$environment.Add('COREHOST_TRACE_VERBOSITY=4')
$serviceKey.SetValue('Environment', $environment.ToArray(), [Microsoft.Win32.RegistryValueKind]::MultiString)

$wpr = Get-Command wpr.exe -ErrorAction SilentlyContinue
$wprArmed = $false
$wprStatus = 'WPR_NOT_AVAILABLE'
if ($null -ne $wpr) {
    & $wpr.Source -profiles $profilePath *> (Join-Path $diagnosticRoot 'wpr-profile-validation.txt')
    if ($LASTEXITCODE -ne 0) {
        $wprStatus = "WPR_PROFILE_VALIDATION_FAILED exitCode=$LASTEXITCODE"
    }
    else {
        & $wpr.Source -addboot "$profilePath!GaltekEarlyBoot.Verbose" -filemode -recordtempto $wprTempPath *> (Join-Path $diagnosticRoot 'wpr-arm.txt')
        if ($LASTEXITCODE -eq 0) {
            $wprArmed = $true
            $wprStatus = 'WPR_BOOT_TRACE_ARMED'
        }
        else {
            $wprStatus = "WPR_BOOT_TRACE_NOT_ARMED exitCode=$LASTEXITCODE"
        }
    }
}
$backupKey.SetValue('WprArmedByKit', [int]$wprArmed, [Microsoft.Win32.RegistryValueKind]::DWord)
$backupKey.SetValue('WprStoppedByCollector', 0, [Microsoft.Win32.RegistryValueKind]::DWord)
$backupKey.SetValue('WprStatus', $wprStatus, [Microsoft.Win32.RegistryValueKind]::String)

$effective = Get-ServiceEnvironmentValue -Key $serviceKey
foreach ($required in @('COREHOST_TRACE=1', "COREHOST_TRACEFILE=$tracePath", 'COREHOST_TRACE_VERBOSITY=4')) {
    if ($effective.Values -notcontains $required) { throw "Per-service host tracing verification failed for $required" }
}

"PASS: per-service .NET 8 host tracing armed for $serviceName."
"Trace file: $tracePath"
"Service state was not changed: $($service.State)"
"WPR: $wprStatus"
'No environment values were printed. Shut down normally; keep Ethernet disconnected; cold boot; do not run Start-Service; then run 07.'

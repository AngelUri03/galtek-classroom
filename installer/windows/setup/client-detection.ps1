#Requires -Version 5.1
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)

$serviceNames = @('GaltekClassroomAgent', 'GaltekClassroomAgentService')
$taskName = 'GaltekClassroomSessionAgent'
$credentialProviderClsid = '{D1A77223-ACAE-4C53-8C52-4FE8B8357E82}'
$expectedProviderName = 'Galtek Classroom Credential Provider'
$programFiles = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles)
$installRoot = Join-Path $programFiles 'Galtek\Classroom\Agent'

function Open-DetectionRegistryKey {
    param([Parameter(Mandatory = $true)][string] $SubKey)
    $base = [Microsoft.Win32.RegistryKey]::OpenBaseKey(
        [Microsoft.Win32.RegistryHive]::LocalMachine,
        [Microsoft.Win32.RegistryView]::Registry64)
    try { return $base.OpenSubKey($SubKey, $false) }
    finally { $base.Dispose() }
}

$serviceEvidence = @()
foreach ($serviceName in $serviceNames) {
    $key = Open-DetectionRegistryKey -SubKey "SYSTEM\CurrentControlSet\Services\$serviceName"
    if ($null -eq $key) { continue }
    try {
        $serviceEvidence += [pscustomobject]@{
            Name = $serviceName
            ImagePath = [string]$key.GetValue('ImagePath', $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
        }
    }
    finally { $key.Dispose() }
}

$taskExists = $false
$taskPath = $null
$taskArguments = $null
$taskWorkingDirectory = $null
$scheduler = New-Object -ComObject 'Schedule.Service'
try {
    $scheduler.Connect()
    $rootFolder = $scheduler.GetFolder('\')
    try {
        $task = $rootFolder.GetTask($taskName)
        $taskExists = $true
        $actions = @($task.Definition.Actions)
        if ($actions.Count -gt 0) {
            $taskPath = [string]$actions[0].Path
            $taskArguments = [string]$actions[0].Arguments
            $taskWorkingDirectory = [string]$actions[0].WorkingDirectory
        }
    }
    catch {
        $hresult = $_.Exception.HResult
        if ($hresult -notin @(-2147024894, -2147216615)) { throw }
    }
}
finally {
    if ($null -ne $scheduler) { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($scheduler) }
}

$providerSubKey = "SOFTWARE\Microsoft\Windows\CurrentVersion\Authentication\Credential Providers\$credentialProviderClsid"
$inprocSubKey = "SOFTWARE\Classes\CLSID\$credentialProviderClsid\InprocServer32"
$providerName = $null
$providerExists = $false
$provider = Open-DetectionRegistryKey -SubKey $providerSubKey
if ($null -ne $provider) {
    try { $providerExists = $true; $providerName = [string]$provider.GetValue('') }
    finally { $provider.Dispose() }
}
$credentialProviderPath = $null
$inproc = Open-DetectionRegistryKey -SubKey $inprocSubKey
if ($null -ne $inproc) {
    try { $credentialProviderPath = [string]$inproc.GetValue('') }
    finally { $inproc.Dispose() }
}
$credentialProviderRegistered = $providerExists -or -not [string]::IsNullOrWhiteSpace($credentialProviderPath)

$state = [pscustomobject]@{
    ServiceExists = @($serviceEvidence | Where-Object Name -eq 'GaltekClassroomAgent').Count -gt 0
    LegacyServiceExists = @($serviceEvidence | Where-Object Name -eq 'GaltekClassroomAgentService').Count -gt 0
    ServiceImagePath = if ($serviceEvidence.Count -gt 0) { [string]$serviceEvidence[0].ImagePath } else { $null }
    SessionTaskExists = $taskExists
    SessionExecutablePath = $taskPath
    SessionArguments = $taskArguments
    SessionWorkingDirectory = $taskWorkingDirectory
    CredentialProviderRegistered = $credentialProviderRegistered
    CredentialProviderPath = $credentialProviderPath
}

if ($providerExists -and -not [string]::Equals($providerName, $expectedProviderName, [StringComparison]::Ordinal)) {
    $output = [ordered]@{
        state = 'BLOCKED_CONFLICT'
        component = 'CP'
        reason = 'PROVIDER_NAME_MISMATCH'
        service = $serviceEvidence.Count -gt 0
        session = $taskExists
        credentialProvider = $credentialProviderRegistered
    }
}
else {
    $plan = Get-GaltekLegacyAdoptionPlan -State $state -InstallRoot $installRoot
    $resolvedState = if (-not $plan.IsLegacyInstallation) {
        'FRESH'
    }
    elseif (-not $plan.IsValid) {
        'BLOCKED_CONFLICT'
    }
    elseif ($serviceEvidence.Count -gt 0 -and $taskExists -and $credentialProviderRegistered) {
        'LEGACY_SUPPORTED'
    }
    else {
        'REPAIRABLE_PARTIAL'
    }
    $output = [ordered]@{
        state = $resolvedState
        component = $plan.Component
        reason = $plan.Reason
        service = $serviceEvidence.Count -gt 0
        session = $taskExists
        credentialProvider = $credentialProviderRegistered
    }
}

$output | ConvertTo-Json -Compress

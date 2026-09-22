#Requires -Version 5.1
[CmdletBinding()]
param(
    [string] $ExpectedExecutablePath = "$([Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles))\Galtek\Classroom\Agent\GaltekClassroom.Agent.Service.exe"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'agent-service-installation-contract.ps1')
. (Join-Path $PSScriptRoot 'agent-service-sc-arguments.ps1')

$serviceName = 'GaltekClassroomAgent'
$service = Get-CimInstance -ClassName Win32_Service -Filter "Name='$serviceName'" -ErrorAction Stop
if ($null -eq $service) { throw "Service $serviceName is not installed." }

$serviceKeyPath = "SYSTEM\CurrentControlSet\Services\$serviceName"
$serviceKey = [Microsoft.Win32.Registry]::LocalMachine.OpenSubKey($serviceKeyPath, $false)
if ($null -eq $serviceKey) { throw "SCM registry key is missing: HKLM\$serviceKeyPath" }

try {
    $registryState = [pscustomobject]@{
        Start = $serviceKey.GetValue('Start', $null)
        DelayedAutostart = $serviceKey.GetValue('DelayedAutostart', $null)
        ObjectName = $serviceKey.GetValue('ObjectName', $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
        ImagePath = $serviceKey.GetValue('ImagePath', $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
        FailureActionsOnNonCrashFailures = $serviceKey.GetValue('FailureActionsOnNonCrashFailures', $null)
        FailureActions = $serviceKey.GetValue('FailureActions', $null)
    }
}
finally {
    $serviceKey.Dispose()
}

$expectedBinaryPathName = New-AgentServiceBinaryPathName -ExecutablePath ([System.IO.Path]::GetFullPath($ExpectedExecutablePath))
Assert-AgentServiceInstallationPolicy `
    -Service $service `
    -RegistryState $registryState `
    -ExpectedBinaryPathName $expectedBinaryPathName

Write-Host 'PASS: Agent Service is Automatic (Delayed Start), LocalSystem, with the expected ImagePath and recovery policy.'

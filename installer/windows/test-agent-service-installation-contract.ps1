#Requires -Version 5.1
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'agent-service-installation-contract.ps1')

function Add-UInt32Bytes {
    param(
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][Collections.Generic.List[byte]] $Bytes,
        [Parameter(Mandatory = $true)][uint32] $Value
    )

    $Bytes.AddRange([BitConverter]::GetBytes($Value))
}

$failureBytes = [Collections.Generic.List[byte]]::new()
foreach ($value in @([uint32]86400, [uint32]0, [uint32]0, [uint32]3, [uint32]20)) {
    Add-UInt32Bytes -Bytes $failureBytes -Value $value
}
foreach ($delay in @(5000, 15000, 60000)) {
    Add-UInt32Bytes -Bytes $failureBytes -Value 1
    Add-UInt32Bytes -Bytes $failureBytes -Value $delay
}

$binaryPathName = '"C:\Program Files\Galtek\Classroom\Agent\GaltekClassroom.Agent.Service.exe"'
$service = [pscustomobject]@{
    StartMode = 'Auto'
    StartName = 'LocalSystem'
    PathName = $binaryPathName
}
$registryState = [pscustomobject]@{
    Start = 2
    DelayedAutostart = 1
    ObjectName = 'LocalSystem'
    ImagePath = $binaryPathName
    FailureActionsOnNonCrashFailures = 1
    FailureActions = $failureBytes.ToArray()
}

Assert-AgentServiceInstallationPolicy -Service $service -RegistryState $registryState -ExpectedBinaryPathName $binaryPathName

$registryState.DelayedAutostart = 0
$exception = $null
try {
    Assert-AgentServiceInstallationPolicy -Service $service -RegistryState $registryState -ExpectedBinaryPathName $binaryPathName
}
catch {
    $exception = $_.Exception
}
if ($null -eq $exception -or $exception.Message -notmatch 'DelayedAutostart=1') {
    throw 'Policy helper did not reject a missing delayed-auto flag.'
}

Write-Host 'Agent Service installation policy helper tests passed.'

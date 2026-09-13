#Requires -Version 5.1
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'agent-service-sc-arguments.ps1')

function Assert-Equal {
    param(
        [Parameter(Mandatory = $true)] $Expected,
        [Parameter(Mandatory = $true)] $Actual,
        [Parameter(Mandatory = $true)][string] $Message
    )

    if ($Expected -ne $Actual) {
        throw "$Message Expected: [$Expected]. Actual: [$Actual]."
    }
}

function Assert-SequenceEqual {
    param(
        [Parameter(Mandatory = $true)][string[]] $Expected,
        [Parameter(Mandatory = $true)][string[]] $Actual,
        [Parameter(Mandatory = $true)][string] $Message
    )

    if ($Expected.Count -ne $Actual.Count) {
        throw "$Message Expected count $($Expected.Count), actual count $($Actual.Count). Actual: $($Actual -join ' | ')"
    }

    for ($index = 0; $index -lt $Expected.Count; $index++) {
        if ($Expected[$index] -ne $Actual[$index]) {
            throw "$Message Mismatch at index $index. Expected: [$($Expected[$index])]. Actual: [$($Actual[$index])]."
        }
    }
}

function Assert-DoesNotContainToken {
    param(
        [Parameter(Mandatory = $true)][string[]] $Arguments,
        [Parameter(Mandatory = $true)][string] $ForbiddenToken,
        [Parameter(Mandatory = $true)][string] $Message
    )

    foreach ($argument in $Arguments) {
        if ($argument -eq $ForbiddenToken) {
            throw "$Message Forbidden token found: [$ForbiddenToken]."
        }
    }
}

function Assert-ScOptionValue {
    param(
        [Parameter(Mandatory = $true)][string[]] $Arguments,
        [Parameter(Mandatory = $true)][string] $Option,
        [Parameter(Mandatory = $true)][string] $Value
    )

    $index = [Array]::IndexOf($Arguments, $Option)
    if ($index -lt 0) {
        throw "Missing sc.exe option token [$Option]."
    }

    if ($index + 1 -ge $Arguments.Count) {
        throw "Missing value after sc.exe option token [$Option]."
    }

    Assert-Equal -Expected $Value -Actual $Arguments[$index + 1] -Message "Unexpected value after [$Option]."
}

$serviceName = 'GaltekClassroomAgent'
$displayName = 'Galtek Classroom Agent Service'
$executablePath = 'C:\Program Files\Galtek\Classroom\Agent\GaltekClassroom.Agent.Service.exe'
$binaryPathName = New-AgentServiceBinaryPathName -ExecutablePath $executablePath
$recoveryActions = 'restart/5000/restart/15000/restart/60000'

Assert-Equal -Expected '"C:\Program Files\Galtek\Classroom\Agent\GaltekClassroom.Agent.Service.exe"' -Actual $binaryPathName -Message 'ImagePath must keep executable quoting.'

$createArguments = @(New-AgentServiceCreateScArguments -ServiceName $serviceName -BinaryPathName $binaryPathName -ServiceDisplayName $displayName)
Assert-SequenceEqual -Expected @(
    'create',
    $serviceName,
    'binPath=',
    $binaryPathName,
    'DisplayName=',
    $displayName,
    'start=',
    'auto',
    'obj=',
    'LocalSystem'
) -Actual $createArguments -Message 'create arguments must use native sc.exe tokenization.'
Assert-ScOptionValue -Arguments $createArguments -Option 'binPath=' -Value $binaryPathName
Assert-ScOptionValue -Arguments $createArguments -Option 'DisplayName=' -Value $displayName
Assert-ScOptionValue -Arguments $createArguments -Option 'start=' -Value 'auto'
Assert-ScOptionValue -Arguments $createArguments -Option 'obj=' -Value 'LocalSystem'

$configArguments = @(New-AgentServiceConfigScArguments -ServiceName $serviceName -BinaryPathName $binaryPathName -ServiceDisplayName $displayName)
Assert-SequenceEqual -Expected @(
    'config',
    $serviceName,
    'binPath=',
    $binaryPathName,
    'DisplayName=',
    $displayName,
    'start=',
    'auto',
    'obj=',
    'LocalSystem'
) -Actual $configArguments -Message 'config arguments must use native sc.exe tokenization.'
Assert-ScOptionValue -Arguments $configArguments -Option 'binPath=' -Value $binaryPathName
Assert-ScOptionValue -Arguments $configArguments -Option 'DisplayName=' -Value $displayName

$failureArguments = @(New-AgentServiceFailureScArguments -ServiceName $serviceName -RecoveryResetSeconds 86400 -RecoveryActions $recoveryActions)
Assert-SequenceEqual -Expected @(
    'failure',
    $serviceName,
    'reset=',
    '86400',
    'actions=',
    $recoveryActions
) -Actual $failureArguments -Message 'failure arguments must keep reset/actions as separate option and value tokens.'
Assert-ScOptionValue -Arguments $failureArguments -Option 'reset=' -Value '86400'
Assert-ScOptionValue -Arguments $failureArguments -Option 'actions=' -Value $recoveryActions

$allArguments = @($createArguments + $configArguments + $failureArguments)
Assert-DoesNotContainToken -Arguments $allArguments -ForbiddenToken "binPath= $binaryPathName" -Message 'sc.exe option/value tokens must not be merged.'
Assert-DoesNotContainToken -Arguments $allArguments -ForbiddenToken "DisplayName= $displayName" -Message 'sc.exe option/value tokens must not be merged.'
Assert-DoesNotContainToken -Arguments $allArguments -ForbiddenToken 'start= auto' -Message 'sc.exe option/value tokens must not be merged.'
Assert-DoesNotContainToken -Arguments $allArguments -ForbiddenToken 'obj= LocalSystem' -Message 'sc.exe option/value tokens must not be merged.'
Assert-DoesNotContainToken -Arguments $allArguments -ForbiddenToken 'reset= 86400' -Message 'sc.exe option/value tokens must not be merged.'
Assert-DoesNotContainToken -Arguments $allArguments -ForbiddenToken "actions= $recoveryActions" -Message 'sc.exe option/value tokens must not be merged.'

Write-Host 'Agent Service sc.exe argument contract tests passed.'

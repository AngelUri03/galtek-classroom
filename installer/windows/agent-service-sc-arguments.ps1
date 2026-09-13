#Requires -Version 5.1
Set-StrictMode -Version Latest

function New-AgentServiceBinaryPathName {
    param([Parameter(Mandatory = $true)][string] $ExecutablePath)

    return '"' + $ExecutablePath + '"'
}

function New-AgentServiceCreateScArguments {
    param(
        [Parameter(Mandatory = $true)][string] $ServiceName,
        [Parameter(Mandatory = $true)][string] $BinaryPathName,
        [Parameter(Mandatory = $true)][string] $ServiceDisplayName
    )

    return @(
        'create',
        $ServiceName,
        'binPath=',
        $BinaryPathName,
        'DisplayName=',
        $ServiceDisplayName,
        'start=',
        'auto',
        'obj=',
        'LocalSystem'
    )
}

function New-AgentServiceConfigScArguments {
    param(
        [Parameter(Mandatory = $true)][string] $ServiceName,
        [Parameter(Mandatory = $true)][string] $BinaryPathName,
        [Parameter(Mandatory = $true)][string] $ServiceDisplayName
    )

    return @(
        'config',
        $ServiceName,
        'binPath=',
        $BinaryPathName,
        'DisplayName=',
        $ServiceDisplayName,
        'start=',
        'auto',
        'obj=',
        'LocalSystem'
    )
}

function New-AgentServiceFailureScArguments {
    param(
        [Parameter(Mandatory = $true)][string] $ServiceName,
        [Parameter(Mandatory = $true)][int] $RecoveryResetSeconds,
        [Parameter(Mandatory = $true)][string] $RecoveryActions
    )

    return @(
        'failure',
        $ServiceName,
        'reset=',
        [string] $RecoveryResetSeconds,
        'actions=',
        $RecoveryActions
    )
}

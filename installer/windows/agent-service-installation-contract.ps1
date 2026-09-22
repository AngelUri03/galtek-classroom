#Requires -Version 5.1
Set-StrictMode -Version Latest

function ConvertFrom-AgentServiceFailureActions {
    param([Parameter(Mandatory = $true)][byte[]] $Value)

    if ($Value.Length -lt 20) {
        throw 'SCM FailureActions registry data is too short.'
    }

    $resetSeconds = [BitConverter]::ToUInt32($Value, 0)
    $actionCount = [BitConverter]::ToUInt32($Value, 12)
    $actionsOffset = [BitConverter]::ToUInt32($Value, 16)
    if ($actionCount -gt 32 -or $actionsOffset -gt $Value.Length) {
        throw 'SCM FailureActions registry data has invalid bounds.'
    }

    $requiredLength = [uint64]$actionsOffset + ([uint64]$actionCount * 8)
    if ($requiredLength -gt $Value.Length) {
        throw 'SCM FailureActions registry data is truncated.'
    }

    $actions = @()
    for ($index = 0; $index -lt $actionCount; $index++) {
        $offset = [int]$actionsOffset + ($index * 8)
        $actions += [pscustomobject]@{
            Type = [BitConverter]::ToUInt32($Value, $offset)
            DelayMilliseconds = [BitConverter]::ToUInt32($Value, $offset + 4)
        }
    }

    return [pscustomobject]@{
        ResetSeconds = $resetSeconds
        Actions = @($actions)
    }
}

function Assert-AgentServiceInstallationPolicy {
    param(
        [Parameter(Mandatory = $true)] $Service,
        [Parameter(Mandatory = $true)] $RegistryState,
        [Parameter(Mandatory = $true)][string] $ExpectedBinaryPathName
    )

    if ($Service.StartMode -ne 'Auto') { throw "Expected StartMode Auto; actual: $($Service.StartMode)." }
    if ($Service.StartName -notin @('LocalSystem', 'Local System')) { throw "Expected LocalSystem; actual: $($Service.StartName)." }
    if ($Service.PathName -cne $ExpectedBinaryPathName) { throw "Unexpected ImagePath. Expected: $ExpectedBinaryPathName. Actual: $($Service.PathName)." }
    if ([int]$RegistryState.Start -ne 2) { throw "Expected SCM Start=2 (Automatic); actual: $($RegistryState.Start)." }
    if ([int]$RegistryState.DelayedAutostart -ne 1) { throw "Expected SCM DelayedAutostart=1; actual: $($RegistryState.DelayedAutostart)." }
    if ($RegistryState.ObjectName -notin @('LocalSystem', 'Local System')) { throw "Expected registry ObjectName LocalSystem; actual: $($RegistryState.ObjectName)." }
    if ($RegistryState.ImagePath -cne $ExpectedBinaryPathName) { throw "Unexpected registry ImagePath. Expected: $ExpectedBinaryPathName. Actual: $($RegistryState.ImagePath)." }
    if ([int]$RegistryState.FailureActionsOnNonCrashFailures -ne 1) { throw 'Expected recovery failureflag=1.' }

    $failurePolicy = ConvertFrom-AgentServiceFailureActions -Value ([byte[]]$RegistryState.FailureActions)
    if ($failurePolicy.ResetSeconds -ne 86400) { throw "Expected recovery reset 86400 seconds; actual: $($failurePolicy.ResetSeconds)." }
    if ($failurePolicy.Actions.Count -ne 3) { throw "Expected three recovery actions; actual: $($failurePolicy.Actions.Count)." }

    $expectedDelays = @(5000, 15000, 60000)
    for ($index = 0; $index -lt $expectedDelays.Count; $index++) {
        if ($failurePolicy.Actions[$index].Type -ne 1 -or $failurePolicy.Actions[$index].DelayMilliseconds -ne $expectedDelays[$index]) {
            throw "Unexpected recovery action $index. Expected restart/$($expectedDelays[$index]); actual type=$($failurePolicy.Actions[$index].Type), delay=$($failurePolicy.Actions[$index].DelayMilliseconds)."
        }
    }
}

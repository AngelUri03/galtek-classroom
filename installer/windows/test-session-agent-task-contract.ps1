#Requires -Version 5.1
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'session-agent-task-contract.ps1')

function Assert-Equal {
    param(
        [AllowNull()] $Expected,
        [AllowNull()] $Actual,
        [Parameter(Mandatory = $true)][string] $Message
    )

    if ($Expected -ne $Actual) {
        throw "$Message Expected: [$Expected]. Actual: [$Actual]."
    }
}

function Assert-DoesNotThrow {
    param(
        [Parameter(Mandatory = $true)][scriptblock] $Script,
        [Parameter(Mandatory = $true)][string] $Message
    )

    try {
        & $Script
    }
    catch {
        throw "$Message Unexpected error: $($_.Exception.Message)"
    }
}

function Assert-Throws {
    param(
        [Parameter(Mandatory = $true)][scriptblock] $Script,
        [Parameter(Mandatory = $true)][string] $Message
    )

    try {
        & $Script
    }
    catch {
        return
    }

    throw "$Message Expected an error."
}

function Assert-TextDoesNotContain {
    param(
        [Parameter(Mandatory = $true)][string] $Text,
        [Parameter(Mandatory = $true)][string] $ForbiddenText,
        [Parameter(Mandatory = $true)][string] $Message
    )

    if ($Text.Contains($ForbiddenText)) {
        throw "$Message Forbidden text found: [$ForbiddenText]."
    }
}

function New-TestSessionAgentTask {
    param(
        [AllowNull()][string] $GroupId = 'S-1-5-32-545',
        [AllowNull()][string] $UserId = $null,
        [string] $RunLevel = 'Limited',
        [string] $TriggerClass = 'MSFT_TaskLogonTrigger',
        [string] $Execute = 'C:\Program Files\Galtek\Classroom\Agent\Session\GaltekClassroom.Agent.Session.exe',
        [string] $Arguments = '--background',
        [string] $MultipleInstances = 'Parallel',
        [bool] $RunOnlyIfNetworkAvailable = $false
    )

    return [pscustomobject]@{
        Principal = [pscustomobject]@{
            GroupId = $GroupId
            UserId = $UserId
            RunLevel = $RunLevel
        }
        Actions = @([pscustomobject]@{
            Execute = $Execute
            Arguments = $Arguments
        })
        Triggers = @([pscustomobject]@{
            CimClass = [pscustomobject]@{
                CimClassName = $TriggerClass
            }
        })
        Settings = [pscustomobject]@{
            MultipleInstances = $MultipleInstances
            RunOnlyIfNetworkAvailable = $RunOnlyIfNetworkAvailable
        }
    }
}

$expectedSid = 'S-1-5-32-545'
$otherSid = 'S-1-5-32-544'
$systemSid = 'S-1-5-18'
$expectedExecutablePath = 'C:\Program Files\Galtek\Classroom\Agent\Session\GaltekClassroom.Agent.Session.exe'

$resolver = {
    param([string] $Identity)

    switch ($Identity) {
        'localized-principal-name' { return 'S-1-5-32-545' }
        'different-principal-name' { return 'S-1-5-32-544' }
        'system-principal-name' { return 'S-1-5-18' }
        default { throw "Cannot resolve test identity: $Identity" }
    }
}

Assert-Equal `
    -Expected $expectedSid `
    -Actual (Resolve-SessionTaskPrincipalSid -Identity $expectedSid) `
    -Message 'Literal SID must remain valid.'

Assert-Equal `
    -Expected $expectedSid `
    -Actual (Resolve-SessionTaskPrincipalSid -Identity 'localized-principal-name' -NtAccountSidResolver $resolver) `
    -Message 'Resolvable non-SID identity must normalize to the expected SID.'

Assert-DoesNotThrow `
    -Script {
        Assert-SessionAgentTaskContract `
            -Task (New-TestSessionAgentTask -GroupId $expectedSid) `
            -ExecutablePath $expectedExecutablePath `
            -ExpectedPrincipalGroupSid $expectedSid
    } `
    -Message 'Task with literal principal SID should be accepted.'

Assert-DoesNotThrow `
    -Script {
        Assert-SessionAgentTaskContract `
            -Task (New-TestSessionAgentTask -GroupId 'localized-principal-name') `
            -ExecutablePath $expectedExecutablePath `
            -ExpectedPrincipalGroupSid $expectedSid `
            -NtAccountSidResolver $resolver
    } `
    -Message 'Task with resolvable non-SID principal should be accepted.'

Assert-Throws `
    -Script {
        Assert-SessionAgentTaskContract `
            -Task (New-TestSessionAgentTask -GroupId 'different-principal-name') `
            -ExecutablePath $expectedExecutablePath `
            -ExpectedPrincipalGroupSid $expectedSid `
            -NtAccountSidResolver $resolver
    } `
    -Message 'Task principal resolving to another SID should be rejected.'

Assert-Throws `
    -Script {
        Assert-SessionAgentTaskContract `
            -Task (New-TestSessionAgentTask -GroupId 'unresolvable-principal-name') `
            -ExecutablePath $expectedExecutablePath `
            -ExpectedPrincipalGroupSid $expectedSid `
            -NtAccountSidResolver $resolver
    } `
    -Message 'Task principal that cannot be resolved should be rejected.'

Assert-Throws `
    -Script {
        Assert-SessionAgentTaskContract `
            -Task (New-TestSessionAgentTask -GroupId $expectedSid -UserId 'system-principal-name') `
            -ExecutablePath $expectedExecutablePath `
            -ExpectedPrincipalGroupSid $expectedSid `
            -NtAccountSidResolver $resolver
    } `
    -Message 'Task user principal resolving to LocalSystem should be rejected.'

Assert-Throws `
    -Script {
        Assert-SessionAgentTaskContract `
            -Task (New-TestSessionAgentTask -RunLevel 'Highest') `
            -ExecutablePath $expectedExecutablePath `
            -ExpectedPrincipalGroupSid $expectedSid
    } `
    -Message 'RunLevel validation should remain active.'

Assert-Throws `
    -Script {
        Assert-SessionAgentTaskContract `
            -Task (New-TestSessionAgentTask -TriggerClass 'MSFT_TaskDailyTrigger') `
            -ExecutablePath $expectedExecutablePath `
            -ExpectedPrincipalGroupSid $expectedSid
    } `
    -Message 'AtLogon trigger validation should remain active.'

Assert-Throws `
    -Script {
        Assert-SessionAgentTaskContract `
            -Task (New-TestSessionAgentTask -Execute 'C:\Other\GaltekClassroom.Agent.Session.exe') `
            -ExecutablePath $expectedExecutablePath `
            -ExpectedPrincipalGroupSid $expectedSid
    } `
    -Message 'Executable validation should remain active.'

Assert-Throws `
    -Script {
        Assert-SessionAgentTaskContract `
            -Task (New-TestSessionAgentTask -Arguments '--foreground') `
            -ExecutablePath $expectedExecutablePath `
            -ExpectedPrincipalGroupSid $expectedSid
    } `
    -Message 'Argument validation should remain active.'

Assert-Throws `
    -Script {
        Assert-SessionAgentTaskContract `
            -Task (New-TestSessionAgentTask -MultipleInstances 'IgnoreNew') `
            -ExecutablePath $expectedExecutablePath `
            -ExpectedPrincipalGroupSid $expectedSid
    } `
    -Message 'MultipleInstances validation should remain active.'

Assert-Throws `
    -Script {
        Assert-SessionAgentTaskContract `
            -Task (New-TestSessionAgentTask -RunOnlyIfNetworkAvailable $true) `
            -ExecutablePath $expectedExecutablePath `
            -ExpectedPrincipalGroupSid $expectedSid
    } `
    -Message 'Network availability validation should remain active.'

$contractSource = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'session-agent-task-contract.ps1') -Raw
Assert-TextDoesNotContain `
    -Text $contractSource `
    -ForbiddenText ('Us' + 'ers') `
    -Message 'Principal validation must not accept a localized group name by literal text.'
Assert-TextDoesNotContain `
    -Text $contractSource `
    -ForbiddenText ('Usu' + 'arios') `
    -Message 'Principal validation must not accept a localized group name by literal text.'

Assert-Equal -Expected $otherSid -Actual (Resolve-SessionTaskPrincipalSid -Identity 'different-principal-name' -NtAccountSidResolver $resolver) -Message 'Different resolvable identity should expose its real SID.'
Assert-Equal -Expected $null -Actual (Resolve-SessionTaskPrincipalSid -Identity 'unresolvable-principal-name' -NtAccountSidResolver $resolver) -Message 'Unresolvable identity should return null.'
Assert-Equal -Expected $systemSid -Actual (Resolve-SessionTaskPrincipalSid -Identity 'system-principal-name' -NtAccountSidResolver $resolver) -Message 'System identity test fixture should resolve by SID.'

Write-Host 'Session Agent scheduled task contract tests passed.'

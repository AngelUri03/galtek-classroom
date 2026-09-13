#Requires -Version 5.1
Set-StrictMode -Version Latest

function Resolve-SessionTaskPrincipalSid {
    param(
        [AllowNull()][string] $Identity,
        [scriptblock] $NtAccountSidResolver
    )

    if ([string]::IsNullOrWhiteSpace($Identity)) {
        return $null
    }

    try {
        $sid = [System.Security.Principal.SecurityIdentifier]::new($Identity)
        return $sid.Value
    }
    catch {
    }

    try {
        $resolvedSid = if ($null -ne $NtAccountSidResolver) {
            & $NtAccountSidResolver $Identity
        }
        else {
            $account = [System.Security.Principal.NTAccount]::new($Identity)
            $account.Translate([System.Security.Principal.SecurityIdentifier])
        }

        if ($resolvedSid -is [System.Security.Principal.SecurityIdentifier]) {
            return $resolvedSid.Value
        }

        if ($resolvedSid -is [string]) {
            return ([System.Security.Principal.SecurityIdentifier]::new($resolvedSid)).Value
        }

        return $null
    }
    catch {
        return $null
    }
}

function Resolve-SessionTaskFullPath {
    param([Parameter(Mandatory = $true)][string] $Path)

    return [System.IO.Path]::GetFullPath($Path)
}

function Assert-SessionAgentTaskContract {
    param(
        [Parameter(Mandatory = $true)] $Task,
        [Parameter(Mandatory = $true)][string] $ExecutablePath,
        [Parameter(Mandatory = $true)][string] $ExpectedPrincipalGroupSid,
        [scriptblock] $NtAccountSidResolver
    )

    $action = @($Task.Actions)[0]
    $trigger = @($Task.Triggers)[0]

    $principalGroupIdentity = $Task.Principal.GroupId
    $principalGroupSid = Resolve-SessionTaskPrincipalSid `
        -Identity $principalGroupIdentity `
        -NtAccountSidResolver $NtAccountSidResolver

    if ($principalGroupSid -ne $ExpectedPrincipalGroupSid) {
        $resolved = if ($null -eq $principalGroupSid) { '<unresolved>' } else { $principalGroupSid }
        throw "Scheduled Task principal must resolve to SID $ExpectedPrincipalGroupSid. Actual identity: $principalGroupIdentity. Resolved SID: $resolved"
    }

    if (-not [string]::IsNullOrWhiteSpace($Task.Principal.UserId)) {
        $principalUserSid = Resolve-SessionTaskPrincipalSid `
            -Identity $Task.Principal.UserId `
            -NtAccountSidResolver $NtAccountSidResolver

        if ($null -eq $principalUserSid) {
            throw "Scheduled Task user principal could not be resolved to SID. Actual: $($Task.Principal.UserId)"
        }

        if ($principalUserSid -eq 'S-1-5-18') {
            throw "Scheduled Task must not run as SYSTEM."
        }
    }

    if ($Task.Principal.RunLevel.ToString() -ne 'Limited') {
        throw "Scheduled Task run level must be Limited. Actual: $($Task.Principal.RunLevel)"
    }

    if ($trigger.CimClass.CimClassName -ne 'MSFT_TaskLogonTrigger') {
        throw "Scheduled Task trigger must be AtLogon. Actual: $($trigger.CimClass.CimClassName)"
    }

    if (-not [string]::Equals((Resolve-SessionTaskFullPath $action.Execute), (Resolve-SessionTaskFullPath $ExecutablePath), [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Scheduled Task executable mismatch. Actual: $($action.Execute)"
    }

    if ($action.Arguments -ne '--background') {
        throw "Scheduled Task argument must be --background. Actual: $($action.Arguments)"
    }

    if ($Task.Settings.MultipleInstances.ToString() -ne 'Parallel') {
        throw "Scheduled Task MultipleInstances must be Parallel. Actual: $($Task.Settings.MultipleInstances)"
    }

    if ($Task.Settings.RunOnlyIfNetworkAvailable) {
        throw "Scheduled Task must not require network availability."
    }
}

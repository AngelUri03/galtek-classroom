#Requires -Version 5.1
[CmdletBinding()]
param(
    [string] $ArtifactPath,
    [switch] $NoStartCurrentSession
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$TaskName = 'GaltekClassroomSessionAgent'
$TaskDescription = 'Galtek Classroom Session Agent'
$SessionExecutableName = 'GaltekClassroom.Agent.Session.exe'
$InteractiveUsersGroupSid = 'S-1-5-32-545'

function Test-IsElevated {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)

    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Resolve-FullPath {
    param([Parameter(Mandatory = $true)][string] $Path)

    return [System.IO.Path]::GetFullPath($Path)
}

function Assert-PathInsideDirectory {
    param(
        [Parameter(Mandatory = $true)][string] $Path,
        [Parameter(Mandatory = $true)][string] $ParentDirectory,
        [Parameter(Mandatory = $true)][string] $Purpose
    )

    $fullPath = Resolve-FullPath $Path
    $fullParent = (Resolve-FullPath $ParentDirectory).TrimEnd('\') + '\'
    $fullPathWithSeparator = $fullPath.TrimEnd('\') + '\'

    if (-not $fullPathWithSeparator.StartsWith($fullParent, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "$Purpose must stay under $fullParent. Actual path: $fullPath"
    }
}

function Stop-SessionAgentTaskIfPresent {
    $task = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
    if ($null -eq $task) {
        return
    }

    Write-Host "Stopping scheduled task $TaskName if it is running..."
    Stop-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
}

function Get-InstalledSessionAgentProcesses {
    param([Parameter(Mandatory = $true)][string] $ExecutablePath)

    $expectedExecutablePath = Resolve-FullPath $ExecutablePath
    $processes = Get-CimInstance Win32_Process -Filter "Name='$SessionExecutableName'" -ErrorAction SilentlyContinue

    foreach ($process in $processes) {
        if ([string]::IsNullOrWhiteSpace($process.ExecutablePath)) {
            continue
        }

        $actualExecutablePath = Resolve-FullPath $process.ExecutablePath
        if ([string]::Equals($actualExecutablePath, $expectedExecutablePath, [System.StringComparison]::OrdinalIgnoreCase)) {
            $process
        }
    }
}

function Stop-InstalledSessionAgentProcesses {
    param([Parameter(Mandatory = $true)][string] $ExecutablePath)

    $processes = @(Get-InstalledSessionAgentProcesses -ExecutablePath $ExecutablePath)
    if ($processes.Count -eq 0) {
        return
    }

    foreach ($process in $processes) {
        Write-Host "Stopping installed Session Agent process PID $($process.ProcessId)..."
        Stop-Process -Id $process.ProcessId -Force -ErrorAction Stop
    }

    $deadline = (Get-Date).AddSeconds(15)
    do {
        $remaining = @(Get-InstalledSessionAgentProcesses -ExecutablePath $ExecutablePath)
        if ($remaining.Count -eq 0) {
            return
        }

        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)

    throw "Installed Session Agent processes did not exit within 15 seconds."
}

function Register-SessionAgentTask {
    param(
        [Parameter(Mandatory = $true)][string] $ExecutablePath,
        [Parameter(Mandatory = $true)][string] $WorkingDirectory
    )

    $action = New-ScheduledTaskAction `
        -Execute $ExecutablePath `
        -Argument '--background' `
        -WorkingDirectory $WorkingDirectory

    $trigger = New-ScheduledTaskTrigger -AtLogOn
    $principal = New-ScheduledTaskPrincipal `
        -GroupId $InteractiveUsersGroupSid `
        -RunLevel Limited

    $settings = New-ScheduledTaskSettingsSet `
        -AllowStartIfOnBatteries `
        -DontStopIfGoingOnBatteries `
        -ExecutionTimeLimit (New-TimeSpan -Seconds 0) `
        -MultipleInstances Parallel `
        -RestartCount 3 `
        -RestartInterval (New-TimeSpan -Minutes 1) `
        -StartWhenAvailable `
        -Hidden `
        -Priority 7

    Register-ScheduledTask `
        -TaskName $TaskName `
        -Action $action `
        -Trigger $trigger `
        -Principal $principal `
        -Settings $settings `
        -Description $TaskDescription `
        -Force | Out-Null
}

function Assert-SessionAgentTask {
    param([Parameter(Mandatory = $true)][string] $ExecutablePath)

    $task = Get-ScheduledTask -TaskName $TaskName -ErrorAction Stop
    $action = @($task.Actions)[0]
    $trigger = @($task.Triggers)[0]

    if ($task.Principal.GroupId -ne $InteractiveUsersGroupSid) {
        throw "Scheduled Task principal must be Builtin Users SID $InteractiveUsersGroupSid. Actual: $($task.Principal.GroupId)"
    }

    if ($task.Principal.UserId -eq 'SYSTEM' -or $task.Principal.UserId -eq 'LocalSystem') {
        throw "Scheduled Task must not run as SYSTEM."
    }

    if ($task.Principal.RunLevel.ToString() -ne 'Limited') {
        throw "Scheduled Task run level must be Limited. Actual: $($task.Principal.RunLevel)"
    }

    if ($trigger.CimClass.CimClassName -ne 'MSFT_TaskLogonTrigger') {
        throw "Scheduled Task trigger must be AtLogon. Actual: $($trigger.CimClass.CimClassName)"
    }

    if (-not [string]::Equals((Resolve-FullPath $action.Execute), (Resolve-FullPath $ExecutablePath), [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Scheduled Task executable mismatch. Actual: $($action.Execute)"
    }

    if ($action.Arguments -ne '--background') {
        throw "Scheduled Task argument must be --background. Actual: $($action.Arguments)"
    }

    if ($task.Settings.MultipleInstances.ToString() -ne 'Parallel') {
        throw "Scheduled Task MultipleInstances must be Parallel. Actual: $($task.Settings.MultipleInstances)"
    }

    if ($task.Settings.RunOnlyIfNetworkAvailable) {
        throw "Scheduled Task must not require network availability."
    }
}

if (-not (Test-IsElevated)) {
    Write-Error 'This installer must be run from an elevated PowerShell session. Open PowerShell as Administrator and run the script again.'
    exit 1
}

$repositoryRoot = Resolve-FullPath (Join-Path $PSScriptRoot '..\..')
$artifactDirectory = if ([string]::IsNullOrWhiteSpace($ArtifactPath)) {
    Resolve-FullPath (Join-Path $repositoryRoot 'artifacts\windows\agent-session')
}
else {
    Resolve-FullPath $ArtifactPath
}

$sourceExecutablePath = Join-Path $artifactDirectory $SessionExecutableName
if (-not (Test-Path -LiteralPath $sourceExecutablePath -PathType Leaf)) {
    throw "Published Session Agent artifact was not found: $sourceExecutablePath. Run installer\windows\publish-agent-session.ps1 first."
}

$programFiles = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles)
$agentRootDirectory = Resolve-FullPath (Join-Path $programFiles 'Galtek\Classroom\Agent')
$installDirectory = Resolve-FullPath (Join-Path $agentRootDirectory 'Session')
$sessionExecutablePath = Join-Path $installDirectory $SessionExecutableName

Assert-PathInsideDirectory -Path $agentRootDirectory -ParentDirectory $programFiles -Purpose 'Agent root directory'
Assert-PathInsideDirectory -Path $installDirectory -ParentDirectory $programFiles -Purpose 'Session Agent install directory'

Stop-SessionAgentTaskIfPresent
Stop-InstalledSessionAgentProcesses -ExecutablePath $sessionExecutablePath

if (Test-Path -LiteralPath $installDirectory) {
    Get-ChildItem -LiteralPath $installDirectory -Force | Remove-Item -Recurse -Force
}
else {
    New-Item -ItemType Directory -Path $installDirectory -Force | Out-Null
}

Copy-Item -Path (Join-Path $artifactDirectory '*') -Destination $installDirectory -Recurse -Force

Register-SessionAgentTask -ExecutablePath $sessionExecutablePath -WorkingDirectory $installDirectory
Assert-SessionAgentTask -ExecutablePath $sessionExecutablePath

if (-not $NoStartCurrentSession) {
    $currentSessionId = [System.Diagnostics.Process]::GetCurrentProcess().SessionId
    if ($currentSessionId -ne 0) {
        try {
            Start-ScheduledTask -TaskName $TaskName -ErrorAction Stop
            Write-Host "Scheduled task start requested for the current session."
        }
        catch {
            Write-Warning "Session Agent task was registered but could not be started immediately: $($_.Exception.Message)"
        }
    }
    else {
        Write-Host "Current process is in Session 0; skipping immediate Session Agent start."
    }
}

Write-Host "Session Agent installed."
Write-Host "TaskName: $TaskName"
Write-Host "PrincipalGroupSid: $InteractiveUsersGroupSid"
Write-Host "RunLevel: Limited"
Write-Host "MultipleInstances: Parallel"
Write-Host "Executable: $sessionExecutablePath"

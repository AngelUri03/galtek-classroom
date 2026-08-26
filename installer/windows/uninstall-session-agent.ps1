#Requires -Version 5.1
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$TaskName = 'GaltekClassroomSessionAgent'
$SessionExecutableName = 'GaltekClassroom.Agent.Session.exe'

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

function Remove-SessionAgentTaskIfPresent {
    $task = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
    if ($null -eq $task) {
        return
    }

    Write-Host "Stopping scheduled task $TaskName if it is running..."
    Stop-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue

    Write-Host "Removing scheduled task $TaskName..."
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false -ErrorAction Stop
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

if (-not (Test-IsElevated)) {
    Write-Error 'This uninstaller must be run from an elevated PowerShell session. Open PowerShell as Administrator and run the script again.'
    exit 1
}

$programFiles = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles)
$agentRootDirectory = Resolve-FullPath (Join-Path $programFiles 'Galtek\Classroom\Agent')
$installDirectory = Resolve-FullPath (Join-Path $agentRootDirectory 'Session')
$sessionExecutablePath = Join-Path $installDirectory $SessionExecutableName

Assert-PathInsideDirectory -Path $agentRootDirectory -ParentDirectory $programFiles -Purpose 'Agent root directory'
Assert-PathInsideDirectory -Path $installDirectory -ParentDirectory $programFiles -Purpose 'Session Agent install directory'

Remove-SessionAgentTaskIfPresent
Stop-InstalledSessionAgentProcesses -ExecutablePath $sessionExecutablePath

if (Test-Path -LiteralPath $installDirectory) {
    Remove-Item -LiteralPath $installDirectory -Recurse -Force
    Write-Host "Removed Session Agent binaries: $installDirectory"
}
else {
    Write-Host "Session Agent binaries were not present: $installDirectory"
}

if ((Test-Path -LiteralPath $agentRootDirectory) -and @(Get-ChildItem -LiteralPath $agentRootDirectory -Force).Count -eq 0) {
    Remove-Item -LiteralPath $agentRootDirectory -Force
}

Write-Host "Session Agent uninstall completed."

#Requires -Version 5.1
[CmdletBinding()]
param(
    [switch] $PurgeData
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$ServiceName = 'GaltekClassroomAgent'
$LegacyServiceNames = @('GaltekClassroomAgentService')

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

function Invoke-ScExe {
    param([Parameter(Mandatory = $true)][string[]] $Arguments)

    & sc.exe @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "sc.exe $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

function Wait-ServiceStatus {
    param(
        [Parameter(Mandatory = $true)][string] $Name,
        [Parameter(Mandatory = $true)][string] $Status,
        [int] $TimeoutSeconds = 30
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)

    do {
        $service = Get-Service -Name $Name -ErrorAction SilentlyContinue
        if ($null -ne $service -and $service.Status.ToString() -eq $Status) {
            return
        }

        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)

    $actual = if ($null -eq $service) { 'NotFound' } else { $service.Status.ToString() }
    throw "Service $Name did not reach status $Status within $TimeoutSeconds seconds. Current status: $actual"
}

function Wait-ServiceDeleted {
    param(
        [Parameter(Mandatory = $true)][string] $Name,
        [int] $TimeoutSeconds = 30
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)

    do {
        $service = Get-Service -Name $Name -ErrorAction SilentlyContinue
        if ($null -eq $service) {
            return
        }

        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)

    throw "Service $Name was not removed within $TimeoutSeconds seconds."
}

function Remove-ServiceIfPresent {
    param([Parameter(Mandatory = $true)][string] $Name)

    $service = Get-Service -Name $Name -ErrorAction SilentlyContinue
    if ($null -eq $service) {
        return
    }

    if ($service.Status -ne [System.ServiceProcess.ServiceControllerStatus]::Stopped) {
        Write-Host "Stopping service $Name..."
        Stop-Service -Name $Name -Force -ErrorAction Stop
        Wait-ServiceStatus -Name $Name -Status 'Stopped' -TimeoutSeconds 30
    }

    Write-Host "Removing service registration $Name..."
    Invoke-ScExe -Arguments @('delete', $Name)
    Wait-ServiceDeleted -Name $Name -TimeoutSeconds 30
}

if (-not (Test-IsElevated)) {
    Write-Error 'This uninstaller must be run from an elevated PowerShell session. Open PowerShell as Administrator and run the script again.'
    exit 1
}

$programFiles = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles)
$commonApplicationData = [Environment]::GetFolderPath([Environment+SpecialFolder]::CommonApplicationData)
$installDirectory = Resolve-FullPath (Join-Path $programFiles 'Galtek\Classroom\Agent')
$dataDirectory = Resolve-FullPath (Join-Path $commonApplicationData 'Galtek\Classroom')

Assert-PathInsideDirectory -Path $installDirectory -ParentDirectory $programFiles -Purpose 'Agent Service install directory'
Assert-PathInsideDirectory -Path $dataDirectory -ParentDirectory $commonApplicationData -Purpose 'Agent data directory'

Remove-ServiceIfPresent -Name $ServiceName
foreach ($legacyServiceName in $LegacyServiceNames) {
    Remove-ServiceIfPresent -Name $legacyServiceName
}

if (Test-Path -LiteralPath $installDirectory) {
    Remove-Item -LiteralPath $installDirectory -Recurse -Force
    Write-Host "Removed binaries: $installDirectory"
}
else {
    Write-Host "Binaries were not present: $installDirectory"
}

if ($PurgeData) {
    Write-Warning 'PurgeData elimina Installation Identity y Commercial License. La instalacion resultante requerira una nueva activacion.'

    if (Test-Path -LiteralPath $dataDirectory) {
        Remove-Item -LiteralPath $dataDirectory -Recurse -Force
        Write-Host "Purged data: $dataDirectory"
    }
    else {
        Write-Host "Data directory was not present: $dataDirectory"
    }
}
else {
    Write-Host "ProgramData preserved: $dataDirectory"
}

Write-Host "Agent Service uninstall completed."

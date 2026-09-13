#Requires -Version 5.1
[CmdletBinding()]
param(
    [string] $ArtifactPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$ServiceName = 'GaltekClassroomAgent'
$LegacyServiceNames = @('GaltekClassroomAgentService')
$ServiceDisplayName = 'Galtek Classroom Agent Service'
$ServiceDescription = 'Servicio local de Galtek Classroom para identidad, licencia y administracion segura del equipo.'
$ServiceExecutableName = 'GaltekClassroom.Agent.Service.exe'
$PreservedInstallSubdirectories = @('Session', 'CredentialProvider')
$RecoveryResetSeconds = 86400
$RecoveryActions = 'restart/5000/restart/15000/restart/60000'

. (Join-Path $PSScriptRoot 'agent-service-sc-arguments.ps1')

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

function Stop-ServiceIfPresent {
    param([Parameter(Mandatory = $true)][string] $Name)

    $service = Get-Service -Name $Name -ErrorAction SilentlyContinue
    if ($null -eq $service) {
        return $false
    }

    if ($service.Status -ne [System.ServiceProcess.ServiceControllerStatus]::Stopped) {
        Write-Host "Stopping service $Name..."
        Stop-Service -Name $Name -Force -ErrorAction Stop
        Wait-ServiceStatus -Name $Name -Status 'Stopped' -TimeoutSeconds 30
    }

    return $true
}

function Clear-AgentServiceInstallDirectory {
    param(
        [Parameter(Mandatory = $true)][string] $InstallDirectory,
        [Parameter(Mandatory = $true)][string[]] $PreservedSubdirectories
    )

    if (-not (Test-Path -LiteralPath $InstallDirectory)) {
        New-Item -ItemType Directory -Path $InstallDirectory -Force | Out-Null
        return
    }

    foreach ($item in Get-ChildItem -LiteralPath $InstallDirectory -Force) {
        if ($item.PSIsContainer -and ($PreservedSubdirectories | Where-Object { [string]::Equals($item.Name, $_, [System.StringComparison]::OrdinalIgnoreCase) })) {
            continue
        }

        Remove-Item -LiteralPath $item.FullName -Recurse -Force
    }
}

if (-not (Test-IsElevated)) {
    Write-Error 'This installer must be run from an elevated PowerShell session. Open PowerShell as Administrator and run the script again.'
    exit 1
}

$repositoryRoot = Resolve-FullPath (Join-Path $PSScriptRoot '..\..')
$artifactDirectory = if ([string]::IsNullOrWhiteSpace($ArtifactPath)) {
    Resolve-FullPath (Join-Path $repositoryRoot 'artifacts\windows\agent-service')
}
else {
    Resolve-FullPath $ArtifactPath
}

$sourceExecutablePath = Join-Path $artifactDirectory $ServiceExecutableName
if (-not (Test-Path -LiteralPath $sourceExecutablePath -PathType Leaf)) {
    throw "Published Agent Service artifact was not found: $sourceExecutablePath. Run installer\windows\publish-agent-service.ps1 first."
}

$programFiles = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles)
$commonApplicationData = [Environment]::GetFolderPath([Environment+SpecialFolder]::CommonApplicationData)
$installDirectory = Resolve-FullPath (Join-Path $programFiles 'Galtek\Classroom\Agent')
$dataDirectory = Resolve-FullPath (Join-Path $commonApplicationData 'Galtek\Classroom')
$serviceExecutablePath = Join-Path $installDirectory $ServiceExecutableName
$binaryPathName = New-AgentServiceBinaryPathName -ExecutablePath $serviceExecutablePath

Assert-PathInsideDirectory -Path $installDirectory -ParentDirectory $programFiles -Purpose 'Agent Service install directory'
Assert-PathInsideDirectory -Path $dataDirectory -ParentDirectory $commonApplicationData -Purpose 'Agent data directory'

foreach ($legacyServiceName in $LegacyServiceNames) {
    if (Stop-ServiceIfPresent -Name $legacyServiceName) {
        Write-Host "Removing legacy service registration $legacyServiceName..."
        Invoke-ScExe -Arguments @('delete', $legacyServiceName)
        Wait-ServiceDeleted -Name $legacyServiceName -TimeoutSeconds 30
    }
}

Stop-ServiceIfPresent -Name $ServiceName | Out-Null

New-Item -ItemType Directory -Path $dataDirectory -Force | Out-Null

Clear-AgentServiceInstallDirectory -InstallDirectory $installDirectory -PreservedSubdirectories $PreservedInstallSubdirectories
Copy-Item -Path (Join-Path $artifactDirectory '*') -Destination $installDirectory -Recurse -Force

$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($null -eq $service) {
    Write-Host "Creating Windows Service $ServiceName..."
    Invoke-ScExe -Arguments (New-AgentServiceCreateScArguments -ServiceName $ServiceName -BinaryPathName $binaryPathName -ServiceDisplayName $ServiceDisplayName)
}
else {
    Write-Host "Configuring existing Windows Service $ServiceName..."
    Invoke-ScExe -Arguments (New-AgentServiceConfigScArguments -ServiceName $ServiceName -BinaryPathName $binaryPathName -ServiceDisplayName $ServiceDisplayName)
}

Invoke-ScExe -Arguments @('description', $ServiceName, $ServiceDescription)
Invoke-ScExe -Arguments (New-AgentServiceFailureScArguments -ServiceName $ServiceName -RecoveryResetSeconds $RecoveryResetSeconds -RecoveryActions $RecoveryActions)
Invoke-ScExe -Arguments @('failureflag', $ServiceName, '1')

Write-Host "Starting service $ServiceName..."
Start-Service -Name $ServiceName -ErrorAction Stop
Wait-ServiceStatus -Name $ServiceName -Status 'Running' -TimeoutSeconds 30

$installedService = Get-CimInstance -ClassName Win32_Service -Filter "Name='$ServiceName'"
Write-Host "Agent Service installed and running."
Write-Host "ServiceName: $ServiceName"
Write-Host "DisplayName: $ServiceDisplayName"
Write-Host "StartMode: $($installedService.StartMode)"
Write-Host "Account: $($installedService.StartName)"
Write-Host "ImagePath: $($installedService.PathName)"
Write-Host "ProgramData: $dataDirectory"
